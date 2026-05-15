using Core;
using Game.Combat.Core;
using Game.Combat.Data;
using Game.Enemy.Data;
using Game.Interaction;
using System;
using UnityEngine;
using UnityEngine.AI;

namespace Game.Enemy
{
    /// <summary>
    /// Autonomous enemy agent implementing the five AI states defined in GDD 5.3.3
    /// and the ICombatTarget contract required by SH_HitboxController (Stage A).
    ///
    /// State machine:
    ///   Patrol    → passive roaming, low detection.
    ///   Search    → lost contact, medium detection, investigates last known position.
    ///   Attack    → player in engage range, cycles approach → combo → recover.
    ///   Evade     → triggered by player Energy Surge (§5.3.3.2) or successful Parry.
    ///   Retreat   → triggered at CriticalHealthThreshold (if RetreatsAtCriticalHealth).
    ///
    /// ICombatTarget implementation:
    ///   ReceiveHit() applies EffectiveDamage to internal HP and PostureDamage to
    ///   posture. Posture breaking triggers the Stagger window. HP at or below zero
    ///   triggers OnDefeated → delivers drop rewards via SH_ResourceDropData.
    ///
    /// Group AI (GDD §5.3.3.6):
    ///   SharedAlert — static per-scene flag. When any enemy spots the player or
    ///   detects Energy Surge, all nearby enemies receive the alert immediately,
    ///   transitioning from Patrol/Search to Attack/Evade.
    ///
    /// Difficulty scaling (GDD §5.3.6):
    ///   SH_DifficultyManager calls ApplyZoneScaling(zoneFactor, difficulty)
    ///   before the first encounter in each zone. This scales HP and AttackCooldown
    ///   within the ±20% floor defined by the GDD.
    ///
    /// Responsibility boundaries:
    ///   OWNS: Enemy FSM, posture, HP, block/parry decision, stagger timer.
    ///   OWNS: Drop reward delivery on death.
    ///   DOES NOT OWN: Damage formula (SH_DamageCalculator).
    ///   DOES NOT OWN: Player state (read-only via SH_PlayerContext reference).
    ///   DOES NOT OWN: Dynamic difficulty metrics (SH_DifficultyManager).
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public class SH_EnemyController : MonoBehaviour, ICombatTarget
    {
        #region Dependencies

        [Header("Data")]
        [Tooltip("Archetype data asset (SH_EnemyData). Drives all behavioral parameters " +
                 "and the combat stats used by SH_DamageCalculator.")]
        [SerializeField] private SH_EnemyData _data;

        [Tooltip("Reference to the player context. Assigned at runtime by the Scene " +
                 "initializer or manually in the Inspector for prototype scenes.")]
        [SerializeField] private SH_PlayerContext _playerContext;

        [Header("Animation")]
        [SerializeField] private Animator _animator;

        [Header("Animator Parameters")]
        [SerializeField] private string _animMoveX = "MoveX";
        [SerializeField] private string _animMoveY = "MoveY";

        [Header("Animation Tuning")]
        [SerializeField] private float _animDamping = 0.1f;

        private int _animMoveXHash;
        private int _animMoveYHash;
        private SH_CaptiveCore _captiveCore;
        private bool _captiveRevealed = false;
        private NavMeshAgent _agent;
        private CharacterController _cc;

        #endregion

        #region Runtime State — Health & Posture

        private float _currentHP;
        private float _currentPosture;
        private bool _isDead;
        private bool _isStaggered;
        private float _staggerTimer;
        private float _postureRegenTimer;

        /// <summary>
        /// Scaled max HP after SH_DifficultyManager.ApplyZoneScaling().
        /// Initialized from SH_EnemyData.ResolvedMaxDurability in Awake().
        /// </summary>
        private float _scaledMaxHP;

        /// <summary>
        /// Scaled attack cooldown after difficulty scaling.
        /// </summary>
        private float _scaledAttackCooldown;

        /// Cached effective attack cooldown after applying difficulty scaling.
        private float _scaledAttackStrength;

        #endregion

        #region Runtime State — Blocking / Parrying

        private bool _isBlocking;
        private bool _isInParryWindow;
        private float _blockTimer;

        #endregion

        #region Runtime State — FSM

        /// <summary>
        /// The five AI states
        /// </summary>
        private enum EnemyState
        {
            Patrol,
            Search,
            Attack,
            Evade,
            Retreat,
            Vulnerable
        }

        private EnemyState _state = EnemyState.Patrol;

        /// <summary>
        /// Last known player world position. Updated whenever the player is in
        /// detection range. Used by Search state as the investigation target.
        /// </summary>
        private Vector3 _lastKnownPlayerPosition;

        /// <summary>
        /// Elapsed time in Search state before giving up and returning to Patrol.
        /// </summary>
        private float _searchTimer;
        private const float SearchTimeout = 8f;

        /// <summary>
        /// Cooldown tracker for the current attack combo.
        /// Reset to _scaledAttackCooldown when a combo completes.
        /// </summary>
        private float _attackCooldownTimer;

        /// <summary>
        /// Current attack within the combo window (0 to _data.ComboLength - 1).
        /// </summary>
        private int _comboHitsRemaining;
        private bool _comboInProgress;
        private float _comboStepTimer;
        private const float ComboStepInterval = 0.6f;

        /// <summary>
        /// Evasion movement target. Set when entering Evade state.
        /// </summary>
        private Vector3 _evadeTarget;
        private float _evadeTimer;
        private float _flankerOrbitSide = 1f;
        private const float EvadeDuration = 2f;

        // ─── SetDestination throttle ──────────────────────────────────────
        // NavMeshAgent.SetDestination() recalculates the full NavMesh path.
        // Calling it every frame (60–120×/s per enemy) adds significant CPU
        // cost from the pathfinding system. We throttle to once per
        // DestinationUpdateInterval seconds AND only when the target has moved
        // more than DestinationMoveThreshold meters since the last submission.
        private float _destinationTimer;
        private Vector3 _lastSubmittedDestination;
        private const float DestinationUpdateInterval = 0.15f;   // max 6–7 updates/s
        private const float DestinationMoveThresholdSqr = 0.2f; // 0.5m² = retrigger if moved >0.5m

        // ─── Death timer (replaces Invoke) ────────────────────────────────
        // MonoBehaviour.Invoke() uses reflection to resolve the method name at
        // runtime and registers a timer that's checked every frame via
        // SendMessage overhead. A plain float timer has zero allocation.
        private bool _pendingDeactivation;
        private float _deactivationTimer;
        private const float DeactivationDelay = 1.5f; // Delay before deactivating the GameObject in seconds

        // ─── Temporary Knockback ───────────────────────────────────────────
        // Applied as an instantaneous velocity change on the NavMeshAgent when hit,
        // then decays over time. This is a simple implementation for Stage A to
        // visualize hit impact.
        private bool _knockbackActive;
        private Vector3 _knockbackVelocity;
        private const float KnockbackDecay = 8f; // Higher = faster decay, 0 = no decay (constant velocity)

        #endregion

        #region Shared Alert (Group AI)

        /// <summary>
        /// Static flag. When any enemy calls BroadcastAlert(), all controllers
        /// in the same scene check this in their next Update() frame and transition
        /// to Attack or Evade accordingly.
        /// Resets when all enemies are defeated or the scene is reloaded.
        /// </summary>
        private static bool s_sharedAlertActive = false;

        /// <summary>
        /// Static reference to the alerted player position for group convergence.
        /// </summary>
        private static Vector3 s_alertPlayerPosition;

        /// <summary>
        /// Broadcasts a shared alert to all enemies in the scene.
        /// Called when an enemy spots the player or detects Energy Surge.
        /// </summary>
        private void BroadcastAlert(Vector3 playerPosition)
        {
            s_sharedAlertActive = true;
            s_alertPlayerPosition = playerPosition;
        }

        /// <summary>
        /// Resets the shared alert state. Called by the scene manager or
        /// when the last enemy is defeated.
        /// </summary>
        public static void ResetSharedAlert()
        {
            s_sharedAlertActive = false;
            s_alertPlayerPosition = Vector3.zero;
        }

        #endregion

        #region ICombatTarget Implementation

        public bool IsStaggered => _isStaggered;
        public bool IsDead => _isDead;
        public bool IsBlocking => _isBlocking;
        public bool IsInParryWindow => _isInParryWindow;
        public Vector3 WorldPosition => transform.position;

        /// <summary>
        /// Receives a combat hit from SH_HitboxController.
        /// Applies EffectiveDamage to HP and PostureDamage to posture.
        /// Evaluates stagger and defeat conditions.
        /// Triggers block/parry decision if the unit has not already committed.
        /// </summary>
        public void ReceiveHit(SH_DamagePayload payload)
        {
            if (_isDead) return;
            
            // --- Death check ---
            if (_currentHP <= 0f && !_isDead)
            {
                Die();
                return;
            }

            // --- Apply HP damage ---
            _currentHP -= payload.EffectiveDamage;
            _currentHP = Mathf.Max(0f, _currentHP);

            // --- Apply posture damage ---
            if (!_isStaggered)
            {
                _currentPosture -= payload.PostureDamage;
                _currentPosture = Mathf.Max(0f, _currentPosture);
            }

            // --- Knockback ---
            if (!payload.WasBlocked && !payload.WasParried && payload.KnockbackImpulse.sqrMagnitude > 0.01f)
            {
                float defenseFactor = Mathf.Max(1f, _data?.CombatStats?.Defense ?? 8f);
                ApplyKnockback(payload.KnockbackImpulse / defenseFactor);
            }

            // --- Stagger check ---
            if (_currentPosture <= 0f && !_isStaggered && !(_data?.CombatStats?.IsStaggerImmune ?? false))
            {
                EnterStagger();
            }

            // --- Reaction: break combo and re-evaluate ---
            if (_comboInProgress)
            {
                _comboInProgress = false;
                _comboHitsRemaining = 0;
                _attackCooldownTimer = _scaledAttackCooldown;
                if (_agent != null && !_knockbackActive)
                {
                    _agent.isStopped = false;
                    TrySetDestination(_playerContext.Transform.position, force: true);
                }
            }

            // --- Captive reveal check ---
            if (!_captiveRevealed && _captiveCore != null)
            {
                float hpFraction = _scaledMaxHP > 0f ? _currentHP / _scaledMaxHP : 0f;
                if (hpFraction <= 0.5f)
                {
                    _captiveRevealed = true;
                    TransitionTo(EnemyState.Vulnerable);
                    _captiveCore.ActivateCaptiveReveal();
                }
            }

            // --- Transition toward Evade if Surge is active ---
            if ((_playerContext?.CombatController?.IsSurgeActive ?? false)
                && _state != EnemyState.Vulnerable)
            {
                TryEvaluateSurgeEvasion();
            }

            // --- 2nd Death check ---
            if (_currentHP <= 0f && !_isDead)
            {
                Die();
                return;
            }
        }

        #endregion

        #region Events

        /// <summary>
        /// Fired when this enemy is defeated.
        /// Parameters: (SH_EnemyController defeated enemy).
        /// Consumed by: wave manager, narrative triggers, difficulty tracker.
        /// </summary>
        public event Action<SH_EnemyController> OnDefeated;

        /// <summary>
        /// Fired when this enemy enters or exits the Stagger state.
        /// Parameters: (bool isNowStaggered).
        /// Consumed by: UI posture bar, audio system.
        /// </summary>
        public event Action<bool> OnStaggerChanged;

        #endregion

        #region Initialization

        /// <summary>
        /// Allows the scene to inject the player context at runtime.
        /// Called by the level initializer or a scene manager component.
        /// </summary>
        public void Initialize(SH_PlayerContext playerContext)
        {
            if (playerContext == null)
            {
#if UNITY_EDITOR
                Debug.LogError($"[SH_EnemyController] Initialize: null playerContext on {gameObject.name}.");
#endif
                return;
            }
            _playerContext = playerContext;
        }

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _cc = GetComponent<CharacterController>();

            if (_animator == null)
                _animator = GetComponentInChildren<Animator>();
            
            _animMoveXHash = Animator.StringToHash(_animMoveX);
            _animMoveYHash = Animator.StringToHash(_animMoveY);
            
            if (_data == null)
            {
#if UNITY_EDITOR
                Debug.LogError($"[SH_EnemyController] SH_EnemyData is not assigned on {gameObject.name}.");
#endif
                return;
            }

            _scaledMaxHP = _data.ResolvedMaxDurability;
            _scaledAttackCooldown = _data.AttackCooldown;
            _scaledAttackStrength = _data.CombatStats != null ? _data.CombatStats.Strength : 1f;
            _captiveCore = GetComponentInChildren<SH_CaptiveCore>(includeInactive: true);
            _currentHP = _scaledMaxHP;
            _currentPosture = _data.ResolvedPostureMax;

            if (_agent != null)
            {
                _agent.speed = _data.PatrolSpeed;
                _agent.angularSpeed = _data.RotationSpeed;
                _agent.stoppingDistance = _data.MeleeAttackRange * 0.9f;
                _agent.updateRotation = false;
            }
        }

        #endregion

        #region Unity Lifecycle

        private void Update()
        {
            // Deactivation timer replaces Invoke(nameof(Deactivate), delay).
            // Invoke uses reflection per call; a float counter has zero overhead.
            if (_pendingDeactivation)
            {
                _deactivationTimer -= Time.deltaTime;
                if (_deactivationTimer <= 0f)
                {
                    _pendingDeactivation = false;
                    gameObject.SetActive(false);
                }
                return; // dead — skip all AI ticks
            }

            if (_data == null) return;

            TickPostureRegen();
            TickStagger();
            TickBlock();

            // Advance the destination throttle timer every frame.
            // Individual ticks call TrySetDestination() instead of SetDestination() directly.
            _destinationTimer += Time.deltaTime;

            UpdateAnimatorMovement();

            // Shared alert check
            if (s_sharedAlertActive && _state == EnemyState.Patrol)
            {
                _lastKnownPlayerPosition = s_alertPlayerPosition;
                TransitionTo(EnemyState.Attack);
            }

            // Temporary knockback handling. If active, applies velocity and decays over time.
            if (_knockbackActive)
                TickKnockback();

            switch (_state)
            {
                case EnemyState.Patrol: TickPatrol(); break;
                case EnemyState.Search: TickSearch(); break;
                case EnemyState.Attack: TickAttack(); break;
                case EnemyState.Evade: TickEvade(); break;
                case EnemyState.Retreat: TickRetreat(); break;
                case EnemyState.Vulnerable: TickVulnerable(); break;
            }
        }

        #endregion

        #region Difficulty Scaling API

        /// <summary>
        /// Applies zone-level difficulty scaling to this enemy's HP and attack cadence.
        /// Called by SH_DifficultyManager before the first encounter in each zone.
        ///
        /// GDD §5.3.6: enemy attributes scale linearly with zone, +10% per zone.
        /// Hard difficulty: HP ×1.2, Attack ×1.3. AI aggressiveness: ×1.5 (→ shorter cooldown).
        /// </summary>
        /// <param name="zoneFactor">
        /// Cumulative zone multiplier (1.0 = zone 1, 1.1 = zone 2, etc.)
        /// </param>
        /// <param name="difficulty">
        /// Active difficulty level. Drives additional multipliers per GDD §5.3.6 table.
        /// </param>
        public void ApplyZoneScaling(float zoneFactor, DifficultyLevel difficulty)
        {
            if (_data == null) return;

            float hpMult = GetHpMultiplier(difficulty) * zoneFactor;
            float aiMult = GetAIMult(difficulty);
            float attackMult = GetAttackMult(difficulty) * zoneFactor;

            _scaledMaxHP = _data.ResolvedMaxDurability * hpMult;
            _currentHP = Mathf.Min(_currentHP, _scaledMaxHP);
            _scaledAttackCooldown = _data.AttackCooldown / Mathf.Max(0.1f, aiMult);
            _scaledAttackStrength = (_data.CombatStats != null ? _data.CombatStats.Strength : 1f) * attackMult;
        }

        private static float GetHpMultiplier(DifficultyLevel d) => d switch
        {
            DifficultyLevel.Easy => 0.8f,
            DifficultyLevel.Normal => 1.0f,
            DifficultyLevel.Hard => 1.2f,
            DifficultyLevel.Nightmare => 1.5f,
            _ => 1.0f
        };

        private static float GetAIMult(DifficultyLevel d) => d switch
        {
            DifficultyLevel.Easy => 0.9f,
            DifficultyLevel.Normal => 1.0f,
            DifficultyLevel.Hard => 1.5f,
            DifficultyLevel.Nightmare => 1.3f,
            _ => 1.0f
        };

        private static float GetAttackMult(DifficultyLevel d) => d switch
        {
            DifficultyLevel.Easy => 0.8f,
            DifficultyLevel.Normal => 1.0f,
            DifficultyLevel.Hard => 1.3f,
            DifficultyLevel.Nightmare => 1.6f,
            _ => 1.0f
        };

        #endregion

        #region FSM State Ticks

        private void TickPatrol()
        {
            if (_playerContext == null || _isStaggered) return;

            float dist = Vector3.Distance(transform.position, _playerContext.Transform.position);

            if (dist <= _data.DetectionRange)
            {
                _lastKnownPlayerPosition = _playerContext.Transform.position;
                BroadcastAlert(_lastKnownPlayerPosition);
                TransitionTo(EnemyState.Search);
            }
        }

        private void TickSearch()
        {
            _searchTimer += Time.deltaTime;

            if (_searchTimer >= SearchTimeout || _isStaggered)
            {
                TransitionTo(EnemyState.Patrol);
                return;
            }

            if (_playerContext != null)
            {
                float dist = Vector3.Distance(transform.position, _playerContext.Transform.position);
                if (dist <= _data.AttackEngageRange)
                {
                    TransitionTo(EnemyState.Attack);
                    return;
                }
                if (dist <= _data.DetectionRange)
                {
                    _lastKnownPlayerPosition = _playerContext.Transform.position;
                }
            }
            // Face destination
            FaceTarget(_lastKnownPlayerPosition);

            // Navigate toward last known position
            TrySetDestination(_lastKnownPlayerPosition);
        }
        
        private void TickAttack()
        {
            if (_playerContext == null) return;

            float dist = Vector3.Distance(transform.position, _playerContext.Transform.position);

            // Lost contact
            if (dist > _data.DetectionRange * 1.5f)
            {
                TransitionTo(EnemyState.Search);
                return;
            }

            // Critical health check
            if (_currentHP / _scaledMaxHP <= _data.CriticalHealthThreshold)
            {
                TransitionTo(_data.RetreatsAtCriticalHealth ? EnemyState.Retreat : EnemyState.Attack);
            }

            // Surge detection → Evade
            if ((_playerContext.CombatController?.IsSurgeActive ?? false) && !_comboInProgress)
            {
                TryEvaluateSurgeEvasion();
                return;
            }

            // Archetype-specific attack behavior
            switch (_data.Archetype)
            {
                case EnemyArchetype.Tank: TickAttackTank(dist); break;
                case EnemyArchetype.Flanker: TickAttackFlanker(dist); break;
                default: TickAttackAssailant(dist); break;
            }
        }

        private void TickEvade()
        {
            _evadeTimer += Time.deltaTime;

            Vector3 awayDir = (transform.position - _playerContext.Transform.position).normalized;
            // Flanker evades sideways, Assailant/Tank evade backward
            if (_data.Archetype == EnemyArchetype.Flanker)
                awayDir = Vector3.Cross(awayDir, Vector3.up).normalized;
            _evadeTarget = transform.position + awayDir * _data.EvasionDistance;

            FaceTarget(_evadeTarget);
            TrySetDestination(_evadeTarget, force: true);

            // Return to Attack when evade is complete or Surge ended
            bool surgeEnded = !(_playerContext?.CombatController?.IsSurgeActive ?? false);
            if (_evadeTimer >= EvadeDuration || surgeEnded)
            {
                TransitionTo(EnemyState.Attack);
            }
        }

        private void TickRetreat()
        {
            if (_playerContext == null) return;

            Vector3 awayDir = (transform.position - _playerContext.Transform.position).normalized;
            Vector3 retreatTarget = transform.position + awayDir * 6f;

            TrySetDestination(retreatTarget);

            float dist = Vector3.Distance(transform.position, _playerContext.Transform.position);
            if (dist > _data.DetectionRange)
                TransitionTo(EnemyState.Patrol);
        }

        private void TickVulnerable()
        {
            if (_agent != null && !_agent.isStopped)
                _agent.isStopped = true;
        }

        #endregion

        #region Archetype-specific Attack Ticks

        private void TickAttackAssailant(float dist)
        {
            if (!_knockbackActive)
                TrySetDestination(_playerContext.Transform.position);

            FaceTarget(_playerContext.Transform.position);

            _attackCooldownTimer -= Time.deltaTime;

            if (dist <= _data.MeleeAttackRange && _attackCooldownTimer <= 0f && !_comboInProgress)
                StartCombo();

            if (_comboInProgress)
                TickCombo();

            EvaluateBlockDecision();
        }

        private void TickAttackTank(float dist)
        {
            float preferredHoldRange = _data.AttackEngageRange * 0.6f;

            // Close to melee only when ready to attack; otherwise hold at mid-range.
            if (_attackCooldownTimer <= 0f && !_comboInProgress)
            {
                if (!_knockbackActive)
                    TrySetDestination(_playerContext.Transform.position);
            }
            else if (dist < preferredHoldRange && !_comboInProgress)
            {
                // Step back to preferred hold range.
                Vector3 awayDir = (transform.position - _playerContext.Transform.position).normalized;
                TrySetDestination(transform.position + awayDir * (preferredHoldRange - dist), force: true);
            }

            FaceTarget(_playerContext.Transform.position);

            _attackCooldownTimer -= Time.deltaTime;

            if (dist <= _data.MeleeAttackRange && _attackCooldownTimer <= 0f && !_comboInProgress)
                StartCombo();

            if (_comboInProgress)
                TickCombo();

            // Tank evaluates block more frequently — BlockProbability is scaled higher in its SH_EnemyData asset.
            EvaluateBlockDecision();
        }

        private void TickAttackFlanker(float dist)
        {
            Vector3 toPlayer = (_playerContext.Transform.position - transform.position).normalized;
            Vector3 lateralDir = Vector3.Cross(Vector3.up, toPlayer) * _flankerOrbitSide;
            Vector3 orbitTarget = _playerContext.Transform.position
                                + lateralDir * _data.EvasionDistance
                                - toPlayer * (_data.MeleeAttackRange * 0.8f);

            float distToOrbit = Vector3.Distance(transform.position, orbitTarget);

            if (distToOrbit > 0.5f && !_comboInProgress)
            {
                TrySetDestination(orbitTarget, force: true);
                FaceTarget(_playerContext.Transform.position);
            }
            else
            {
                FaceTarget(_playerContext.Transform.position);
            }

            _attackCooldownTimer -= Time.deltaTime;

            if (dist <= _data.MeleeAttackRange && _attackCooldownTimer <= 0f && !_comboInProgress)
            {
                StartCombo();
                // Flip orbit side after each attack to circle to the opposite flank.
                _flankerOrbitSide *= -1f;
            }

            if (_comboInProgress)
                TickCombo();
        }

        #endregion

        #region Combat Actions

        private void StartCombo()
        {
            _comboInProgress = true;
            _comboHitsRemaining = _data.ComboLength;
            _comboStepTimer = 0f;

            if (_agent != null) _agent.isStopped = true;
        }

        private void TickCombo()
        {
            _comboStepTimer += Time.deltaTime;

            if (_comboStepTimer < ComboStepInterval) return;
            _comboStepTimer = 0f;

            if (_comboHitsRemaining > 0)
            {
                ExecuteSingleAttack();
                _comboHitsRemaining--;
            }

            if (_comboHitsRemaining <= 0)
            {
                _comboInProgress = false;
                _attackCooldownTimer = _scaledAttackCooldown;
                if (_agent != null) _agent.isStopped = false;
            }
        }

        /// <summary>
        /// Executes a single attack hit within the combo sequence.
        /// Calculates final damage based on the enemy's scaled attack strength,
        /// the player's defense, and any active Surge effects. Applies damage to the
        /// player's health and notifies the interaction system of the hit.
        /// </summary>
        private void ExecuteSingleAttack()
        {
            if (_playerContext == null || _data?.CombatStats == null) return;

            float playerDefense = _playerContext.PlayerCombatStats?.Defense ?? 0f;
            float defEffectiveness = _playerContext.CombatSettings?.defenseEffectiveness ?? 0.5f;

            float finalDmg = Mathf.Max(0f, _scaledAttackStrength - playerDefense * defEffectiveness);

            // Apply Surge defense multiplier — Surge actively reduces incoming damage.
            if (_playerContext.CombatController?.IsSurgeActive ?? false)
            {
                float surgeDefMult = _playerContext.CombatSettings?.surgeDefenseMultiplier ?? 1.3f;
                finalDmg /= surgeDefMult;
            }

            _playerContext.Health.TakeDamage(finalDmg);
            _playerContext.Interaction?.NotifyDamageReceived();

#if UNITY_EDITOR
            Debug.Log($"[SH_EnemyController] {_data.DisplayName} attacked player: " +
                      $"{finalDmg:F1} damage (strength={_scaledAttackStrength:F1}, " +
                      $"defense={playerDefense:F1}, surge={_playerContext.CombatController?.IsSurgeActive}).");
#endif
        }

        private void EvaluateBlockDecision()
        {
            if (_isBlocking || _isStaggered) return;
            if (UnityEngine.Random.value < _data.BlockProbability * Time.deltaTime)
            {
                StartBlock();
            }
        }

        private void StartBlock()
        {
            _isBlocking = true;
            _blockTimer = _data.BlockDuration;
            _isInParryWindow = UnityEngine.Random.value < _data.ParryUpgradeProbability;
        }

        private void TickBlock()
        {
            if (!_isBlocking) return;
            _blockTimer -= Time.deltaTime;
            if (_blockTimer <= 0f)
            {
                _isBlocking = false;
                _isInParryWindow = false;
            }
        }

        private void TryEvaluateSurgeEvasion()
        {
            if (UnityEngine.Random.value < _data.SurgeEvadeProbability)
            {
                if (_playerContext != null)
                {
                    Vector3 awayDir = (transform.position - _playerContext.Transform.position).normalized;
                    // Flanker evades sideways, Assailant/Tank evade backward
                    if (_data.Archetype == EnemyArchetype.Flanker)
                        awayDir = Vector3.Cross(awayDir, Vector3.up).normalized;
                    _evadeTarget = transform.position + awayDir * _data.EvasionDistance;
                }
                TransitionTo(EnemyState.Evade);
            }
        }

        #endregion

        #region Stagger & Posture

        private void EnterStagger()
        {
            _comboInProgress = false;
            _comboHitsRemaining = 0;
            _attackCooldownTimer = _scaledAttackCooldown;

            _isStaggered = true;
            _isBlocking = false;
            _isInParryWindow = false;
            _staggerTimer = 0f;
            if (_agent != null) _agent.isStopped = true;
            OnStaggerChanged?.Invoke(true);
#if UNITY_EDITOR
            Debug.Log($"[SH_EnemyController] {_data.DisplayName} staggered.");
#endif
        }

        private void TickStagger()
        {
            if (!_isStaggered) return;

            _staggerTimer += Time.deltaTime;
            if (_playerContext?.CombatSettings == null) return;

            if (_staggerTimer >= _playerContext.CombatSettings.staggerDuration)
            {
                _isStaggered = false;
                _currentPosture = _data.ResolvedPostureMax;
                if (_agent != null && !_knockbackActive)
                    _agent.isStopped = false;
                OnStaggerChanged?.Invoke(false);
            }
#if UNITY_EDITOR
            Debug.Log($"[SH_EnemyController] {_data.DisplayName} _isStaggered {_isStaggered}, _agent.isOnNavMesh: {_agent.isOnNavMesh}");
#endif
        }

        private void TickPostureRegen()
        {
            if (_isStaggered || _currentPosture >= _data.ResolvedPostureMax) return;

            _postureRegenTimer += Time.deltaTime;
            if (_postureRegenTimer < 0.5f) return; // 0.5s grace period before regen starts
            _postureRegenTimer = 0f;

            float regenRate = _playerContext?.CombatSettings?.postureRegenRate ?? 8f;
            _currentPosture = Mathf.Min(_data.ResolvedPostureMax,
                _currentPosture + regenRate * Time.deltaTime);
        }

        #endregion

        #region Death

        private void Die()
        {
            _isDead = true;

            if (_agent != null) _agent.isStopped = true;

            // Deliver economic rewards to the player
            if (_data?.DropData != null && _playerContext?.Resources != null)
            {
                if (_captiveCore != null && _captiveRevealed && _captiveCore.IsAvailable)
                {
                    _captiveCore.ForceDestroy(_playerContext);
                }
                else if (_data?.DropData != null && _playerContext?.Resources != null)
                {
                    _data.DropData.DeliverDestroyRewards(_playerContext.Resources);
                }
            }

            // Elite encounter: roll Energy Flux event (GDD §5.3.2)
            if (_data?.IsElite ?? false)
            {
                _playerContext?.EconomicEvents?.RollEnergyEventOnEliteEncounter();
            }

            OnDefeated?.Invoke(this);

            // Replace Invoke(nameof(Deactivate), 1.5f) with a plain float timer.
            // Invoke() uses reflection to resolve the method name at the call site
            // and registers a SendMessage-style callback checked every frame.
            // A bool+float timer has zero allocation and zero reflection overhead.
            _pendingDeactivation = true;
            _deactivationTimer = DeactivationDelay;
        }

        #endregion

        #region Utility

        /// <summary>
        /// Throttled wrapper around NavMeshAgent.SetDestination().
        /// NavMesh path recalculation is expensive — calling it every frame
        /// (60–120 Hz) with 6+ enemies adds several ms of pathfinding work
        /// per frame. This method submits a new path only when:
        ///   a) The throttle interval has elapsed (≥DestinationUpdateInterval), AND
        ///   b) The requested destination has moved more than the threshold
        ///      (DestinationMoveThresholdSqr) since the last submission.
        /// Movement remains smooth because NavMeshAgent continues traversing the
        /// existing path between updates.
        /// </summary>
        private void TrySetDestination(Vector3 destination, bool force = false)
        {
            if (_agent == null || !_agent.isOnNavMesh) return;
            if (!force)
            {
                if (_destinationTimer < DestinationUpdateInterval) return;
                float moveSqr = (destination - _lastSubmittedDestination).sqrMagnitude;
                if (moveSqr < DestinationMoveThresholdSqr && Vector3.Distance(transform.position, _lastSubmittedDestination) <= _data.MeleeAttackRange) return;
            }
            _agent.SetDestination(destination);
            _lastSubmittedDestination = destination;
            _destinationTimer = 0f;
        }

        private void TransitionTo(EnemyState newState)
        {
            if (_state == newState) return;

            // Exit cleanup
            switch (_state)
            {
                case EnemyState.Search:
                    _searchTimer = 0f;
                    break;
                case EnemyState.Evade:
                    _evadeTimer = 0f;
                    break;
            }

            _state = newState;

            // Entry setup
            switch (_state)
            {
                case EnemyState.Patrol:
                    if (_agent != null) _agent.speed = _data.PatrolSpeed;
                    break;
                case EnemyState.Attack:
                    if (_agent != null) _agent.speed = _data.PursuitSpeed;
                    break;
                case EnemyState.Evade:
                    if (_agent != null) _agent.speed = _data.PursuitSpeed * 1.2f;
                    _evadeTimer = 0f;
                    break;
                case EnemyState.Vulnerable:
                    if (_agent != null) _agent.isStopped = true;
                    break;
            }
        }

        private void TickKnockback()
        {
            if (!_knockbackActive) return;

            // Apply knockback velocity to the agent's position. This is a simple implementation
            transform.position += _knockbackVelocity * Time.deltaTime;

            // Decay knockback velocity over time
            _knockbackVelocity = Vector3.Lerp(_knockbackVelocity, Vector3.zero, KnockbackDecay * Time.deltaTime);

            if (_knockbackVelocity.sqrMagnitude < 0.01f)
            {
                _knockbackActive = false;
                _knockbackVelocity = Vector3.zero;

                if (_cc != null) _cc.enabled = true;
                // After knockback ends, warp the agent to the current position to reset NavMeshAgent's internal state.
                if (_agent != null && _agent.isOnNavMesh)
                {
                    _agent.Warp(transform.position);
                    _agent.ResetPath();
                    if (!_isDead && !_isStaggered)
                    {
                        _agent.isStopped = false;
                        TrySetDestination(_playerContext.Transform.position, force: true);
                    }
                }
            }
        }

        private void ApplyKnockback(Vector3 impulse)
        {
            if (impulse.sqrMagnitude < 0.01f) return;

            // Immediately apply the impulse to the agent's velocity. This is a simple implementation for Stage A.
            if (_agent != null) _agent.isStopped = true;

            if (_cc != null) _cc.enabled = false;

            _knockbackVelocity = impulse;
            _knockbackActive = true;
        }

        private void FaceTarget(Vector3 target)
        {
            Vector3 dir = (target - transform.position);
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) return;

            Quaternion targetRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, targetRot,
                _data.RotationSpeed * Time.deltaTime);
        }

        /// <summary>
        /// Updates the Animator parameters for movement based on the NavMeshAgent's velocity.
        /// Converts world velocity to local space, normalizes it, and applies damping for smooth transitions.
        /// </summary>
        private void UpdateAnimatorMovement()
        {
            if (_animator == null || _agent == null) return;

            Vector3 velocity = _knockbackActive ? _knockbackVelocity : _agent.velocity;
            Vector3 localVelocity = transform.InverseTransformDirection(velocity);

            float x = localVelocity.x;
            float y = localVelocity.z;

            Vector2 normalized = Vector2.ClampMagnitude(new Vector2(x, y), 1f);

            _animator.SetFloat(_animMoveXHash, normalized.x, _animDamping, Time.deltaTime);
            _animator.SetFloat(_animMoveYHash, normalized.y, _animDamping, Time.deltaTime);
        }

        #endregion

        #region Public Query API

        /// <summary>
        /// Normalized HP fraction (0–1). Consumed by SH_Debugger telemetry and UI.
        /// </summary>
        public float NormalizedHP => _scaledMaxHP > 0f ? _currentHP / _scaledMaxHP : 0f;

        /// <summary>
        /// Normalized posture fraction (0–1). Consumed by SH_Debugger telemetry and UI.
        /// </summary>
        public float NormalizedPosture =>
            _data?.ResolvedPostureMax > 0f
                ? _currentPosture / _data.ResolvedPostureMax
                : 0f;

        /// <summary>
        /// Current FSM state label. Exposed for SH_Debugger.
        /// </summary>
        public string CurrentStateName => _state.ToString();

        /// <summary>
        /// Whether this enemy is an Elite variant.
        /// Exposed as a public property so SH_HitboxController can read it
        /// directly without Reflection into the private _data field.
        /// </summary>
        public bool IsElite => _data != null && _data.IsElite;

        /// <summary>
        /// 
        /// </summary>
        public bool IsNockdback => _knockbackActive;

        /// <summary>
        /// 
        /// </summary>
        public Vector3 Destination => _lastSubmittedDestination;

        public float Distance => Vector3.Distance(transform.position, _lastSubmittedDestination);

        /// <summary>
        /// Base combat stats for this enemy archetype.
        /// Exposed as a public property so SH_HitboxController can pass
        /// defenderStats to SH_DamageCalculator.BuildPayload() without calling
        /// GetComponent on a ScriptableObject — which Unity does not support
        /// (ArgumentException: GetComponent requires Component or interface).
        /// SH_CombatStats is a ScriptableObject and cannot be attached to a
        /// GameObject as a component; it must be read via this accessor.
        /// </summary>
        public SH_CombatStats CombatStats => _data?.CombatStats;

        /// <summary>
        /// Injects a player context reference without requiring re-initialization.
        /// Used when the player context is rebuilt mid-scene.
        /// </summary>
        public void SetPlayerContext(SH_PlayerContext ctx) => _playerContext = ctx;

        public void ResetEnemy(SH_PlayerContext playerContext)
        {
            if (_data == null) return;

            _playerContext = playerContext;

            _currentHP = _scaledMaxHP;
            _currentPosture = _data.ResolvedPostureMax;

            _isDead = false;
            _isStaggered = false;
            _isBlocking = false;
            _isInParryWindow = false;
            _comboInProgress = false;
            _comboHitsRemaining = 0;
            _attackCooldownTimer = 0f;
            _knockbackActive = false;
            _knockbackVelocity = Vector3.zero;
            _pendingDeactivation = false;
            _captiveRevealed = false;

            if (_captiveCore != null)
            {
                _captiveCore.ResetCaptiveState();
            }

            if (_agent != null)
            {
                _agent.isStopped = false;
                _agent.speed = _data.PatrolSpeed;
            }

            TransitionTo(EnemyState.Patrol);
            gameObject.SetActive(true);
        }

        #endregion

        #region Gizmos

        private void OnDrawGizmosSelected()
        {
            if (_data == null) return;

            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, _data.DetectionRange);

            Gizmos.color = new Color(1f, 0.4f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, _data.AttackEngageRange);

            Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, _data.MeleeAttackRange);
        }

        #endregion
    }
}