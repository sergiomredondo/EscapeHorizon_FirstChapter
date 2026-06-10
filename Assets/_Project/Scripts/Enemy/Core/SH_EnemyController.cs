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
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public class SH_EnemyController : MonoBehaviour, ICombatTarget
    {
        #region Dependencies

        [Header("Data")]
        [SerializeField] private SH_EnemyData _data;
        [SerializeField] private SH_PlayerContext _playerContext;

        [Header("Animation")]
        [SerializeField] private Animator _animator;

        [Header("Animator Parameters")]
        [SerializeField] private string _animMoveX = "MoveX";
        [SerializeField] private string _animMoveY = "MoveY";
        [SerializeField] private string _animAttackTrigger = "Attack";
        [SerializeField] private string _animDefeatedTrigger = "Defeated";
        [SerializeField] private string _animDetectedParam = "Detected";
        [SerializeField] private string _animRollX = "RollX";
        [SerializeField] private string _animRollY = "RollY";

        [Header("Animation Tuning")]
        [SerializeField] private float _animDamping = 0.1f;

        [Header("Patrol Area")]
        [Tooltip("Maximum radius from the spawn origin within which patrol destinations are chosen.")]
        [Min(1f)]
        [SerializeField] private float _patrolRadius = 8f;
        [Tooltip("Minimum seconds the agent waits at a patrol destination before moving again.")]
        [Min(0f)]
        [SerializeField] private float _patrolWaitTimeMin = 1.5f;
        [Tooltip("Maximum seconds the agent waits at a patrol destination before moving again.")]
        [Min(0f)]
        [SerializeField] private float _patrolWaitTimeMax = 4f;
        [Tooltip("Maximum seconds allowed to reach a patrol destination before picking a new one.")]
        [Min(1f)]
        [SerializeField] private float _patrolStuckTimeout = 6f;
        [Tooltip("NavMesh sample radius used when validating a random patrol destination.")]
        [Min(0.5f)]
        [SerializeField] private float _patrolNavMeshSampleRadius = 2f;

        private int _animMoveXHash;
        private int _animMoveYHash;
        private int _animAttackHash;
        private int _animDefeatedHash;
        private int _animDetectedHash;
        private int _animRollXHash;
        private int _animRollYHash;

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
        private float _scaledMaxHP;
        private float _scaledAttackCooldown;
        private float _scaledAttackStrength;

        #endregion

        #region Runtime State — Blocking / Parrying

        private bool _isBlocking;
        private bool _isInParryWindow;
        private float _blockTimer;

        #endregion

        #region Runtime State — FSM

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
        private Vector3 _lastKnownPlayerPosition;
        private float _searchTimer;
        private const float SearchTimeout = 8f;
        private float _attackCooldownTimer;
        private int _comboHitsRemaining;
        private bool _comboInProgress;
        private float _comboStepTimer;
        private const float ComboStepInterval = 0.6f;
        private Vector3 _evadeTarget;
        private float _evadeTimer;
        private float _flankerOrbitSide = 1f;
        private const float EvadeDuration = 2f;

        private float _destinationTimer;
        private Vector3 _lastSubmittedDestination;
        private const float DestinationUpdateInterval = 0.15f;
        private const float DestinationMoveThresholdSqr = 0.2f;

        private bool _pendingDeactivation;
        private float _deactivationTimer;
        private const float DeactivationDelay = 1.5f;

        private bool _knockbackActive;
        private Vector3 _knockbackVelocity;
        private const float KnockbackDecay = 8f;

        // Tank-specific: tracks whether the Detected boolean has been set.
        private bool _tankDetected = false;

        #endregion

        #region Runtime State — Patrol

        private Vector3 _patrolOrigin;
        private Vector3 _patrolDestination;
        private float _patrolWaitTimer;
        private float _patrolWaitDuration;
        private float _patrolMoveTimer;
        private bool _isPatrolWaiting;

        #endregion

        #region Shared Alert (Group AI)

        private static bool s_sharedAlertActive = false;
        private static Vector3 s_alertPlayerPosition;

        private void BroadcastAlert(Vector3 playerPosition)
        {
            s_sharedAlertActive = true;
            s_alertPlayerPosition = playerPosition;
        }

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

        public void ReceiveHit(SH_DamagePayload payload)
        {
            if (_isDead) return;

            if (_currentHP <= 0f && !_isDead) { Die(); return; }

            _currentHP -= payload.EffectiveDamage;
            _currentHP = Mathf.Max(0f, _currentHP);

            if (!_isStaggered)
            {
                _currentPosture -= payload.PostureDamage;
                _currentPosture = Mathf.Max(0f, _currentPosture);
            }

            if (!payload.WasBlocked && !payload.WasParried
                && payload.KnockbackImpulse.sqrMagnitude > 0.01f)
            {
                float defenseFactor = Mathf.Max(1f, _data?.CombatStats?.Defense ?? 8f);
                ApplyKnockback(payload.KnockbackImpulse / defenseFactor);
            }

            if (_currentPosture <= 0f && !_isStaggered
                && !(_data?.CombatStats?.IsStaggerImmune ?? false))
                EnterStagger();

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

            if ((_playerContext?.CombatController?.IsSurgeActive ?? false)
                && _state != EnemyState.Vulnerable)
                TryEvaluateSurgeEvasion();

            if (_currentHP <= 0f && !_isDead) { Die(); return; }
        }

        #endregion

        #region Events

        public event Action<SH_EnemyController> OnDefeated;
        public event Action<bool> OnStaggerChanged;

        #endregion

        #region Initialization

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
            _animAttackHash = Animator.StringToHash(_animAttackTrigger);
            _animDefeatedHash = Animator.StringToHash(_animDefeatedTrigger);
            _animDetectedHash = Animator.StringToHash(_animDetectedParam);
            _animRollXHash = Animator.StringToHash(_animRollX);
            _animRollYHash = Animator.StringToHash(_animRollY);

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
            _patrolOrigin = transform.position;

            if (_agent != null)
            {
                _agent.speed = _data.PatrolSpeed;
                _agent.angularSpeed = _data.RotationSpeed;
                _agent.stoppingDistance = _data.MeleeAttackRange * 0.9f;
                _agent.updateRotation = false;
            }

            PickPatrolDestination();
        }

        #endregion

        #region Unity Lifecycle

        private void Update()
        {
            if (_pendingDeactivation)
            {
                _deactivationTimer -= Time.deltaTime;
                if (_deactivationTimer <= 0f)
                {
                    _pendingDeactivation = false;
                    gameObject.SetActive(false);
                }
                return;
            }

            if (_data == null) return;

            TickPostureRegen();
            TickStagger();
            TickBlock();

            _destinationTimer += Time.deltaTime;

            UpdateAnimatorMovement();

            if (s_sharedAlertActive && _state == EnemyState.Patrol)
            {
                _lastKnownPlayerPosition = s_alertPlayerPosition;
                TransitionTo(EnemyState.Attack);
            }

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

        public void ApplyZoneScaling(float zoneFactor, DifficultyLevel difficulty)
        {
            if (_data == null) return;

            float hpMult = GetHpMultiplier(difficulty) * zoneFactor;
            float aiMult = GetAIMult(difficulty);
            float attackMult = GetAttackMult(difficulty) * zoneFactor;

            _scaledMaxHP = _data.ResolvedMaxDurability * hpMult;
            _currentHP = Mathf.Min(_currentHP, _scaledMaxHP);
            _scaledAttackCooldown = _data.AttackCooldown / Mathf.Max(0.1f, aiMult);
            _scaledAttackStrength = (_data.CombatStats != null
                ? _data.CombatStats.Strength : 1f) * attackMult;
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
            if (_isStaggered) return;

            if (_playerContext != null)
            {
                float dist = Vector3.Distance(transform.position, _playerContext.Transform.position);
                if (dist <= _data.DetectionRange)
                {
                    _lastKnownPlayerPosition = _playerContext.Transform.position;

                    if (_data.Archetype == EnemyArchetype.Tank)
                    {
                        // Tank sets Detected and begins its hold-distance tracking phase.
                        SetTankDetected(true);
                        BroadcastAlert(_lastKnownPlayerPosition);
                        TransitionTo(EnemyState.Search);
                    }
                    else
                    {
                        BroadcastAlert(_lastKnownPlayerPosition);
                        TransitionTo(EnemyState.Search);
                    }
                    return;
                }
            }

            if (_isPatrolWaiting)
            {
                _patrolWaitTimer += Time.deltaTime;
                if (_patrolWaitTimer >= _patrolWaitDuration)
                {
                    _isPatrolWaiting = false;
                    _patrolMoveTimer = 0f;
                    PickPatrolDestination();
                }
                return;
            }

            _patrolMoveTimer += Time.deltaTime;
            TrySetDestination(_patrolDestination);
            FaceTarget(_patrolDestination);

            float distToDest = Vector3.Distance(transform.position, _patrolDestination);
            bool arrived = distToDest <= _agent.stoppingDistance + 0.3f;
            bool stuck = _patrolMoveTimer >= _patrolStuckTimeout;

            if (arrived || stuck)
            {
                _isPatrolWaiting = true;
                _patrolWaitTimer = 0f;
                _patrolWaitDuration = UnityEngine.Random.Range(
                    Mathf.Min(_patrolWaitTimeMin, _patrolWaitTimeMax),
                    Mathf.Max(_patrolWaitTimeMin, _patrolWaitTimeMax));

                if (_agent != null) _agent.isStopped = true;
            }
        }

        private void TickSearch()
        {
            // Tank has its own tracking search behaviour.
            if (_data?.Archetype == EnemyArchetype.Tank && _tankDetected)
            {
                TickSearchTank();
                return;
            }

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
                    _lastKnownPlayerPosition = _playerContext.Transform.position;
            }

            FaceTarget(_lastKnownPlayerPosition);
            TrySetDestination(_lastKnownPlayerPosition);
        }

        // Tank-specific search: holds at half detection range, waits for engage range.
        private void TickSearchTank()
        {
            if (_playerContext == null) return;

            float dist = Vector3.Distance(transform.position, _playerContext.Transform.position);
            float holdDistance = _data.DetectionRange * 0.5f;

            // Player fully escaped — return to patrol.
            if (dist > _data.DetectionRange)
            {
                SetTankDetected(false);
                TransitionTo(EnemyState.Patrol);
                return;
            }

            // Player entered engage range — start attacking.
            if (dist <= _data.AttackEngageRange)
            {
                TransitionTo(EnemyState.Attack);
                return;
            }

            _lastKnownPlayerPosition = _playerContext.Transform.position;
            FaceTarget(_playerContext.Transform.position);

            // Close the gap to hold distance; stop once within it.
            if (dist > holdDistance)
            {
                if (_agent != null) _agent.isStopped = false;
                TrySetDestination(_playerContext.Transform.position);
            }
            else
            {
                if (_agent != null) _agent.isStopped = true;
            }
        }

        private void TickAttack()
        {
            if (_playerContext == null) return;

            float dist = Vector3.Distance(transform.position, _playerContext.Transform.position);

            if (dist > _data.DetectionRange * 1.5f)
            {
                // Tank resets Detected when it fully loses the player.
                if (_data.Archetype == EnemyArchetype.Tank)
                    SetTankDetected(false);

                TransitionTo(EnemyState.Search);
                return;
            }

            if (_currentHP / _scaledMaxHP <= _data.CriticalHealthThreshold)
                TransitionTo(_data.RetreatsAtCriticalHealth ? EnemyState.Retreat : EnemyState.Attack);

            if ((_playerContext.CombatController?.IsSurgeActive ?? false) && !_comboInProgress)
            {
                TryEvaluateSurgeEvasion();
                return;
            }

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
            if (_data.Archetype == EnemyArchetype.Flanker)
                awayDir = Vector3.Cross(awayDir, Vector3.up).normalized;

            _evadeTarget = transform.position + awayDir * _data.EvasionDistance;
            FaceTarget(_evadeTarget);
            TrySetDestination(_evadeTarget, force: true);

            bool surgeEnded = !(_playerContext?.CombatController?.IsSurgeActive ?? false);
            if (_evadeTimer >= EvadeDuration || surgeEnded)
                TransitionTo(EnemyState.Attack);
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

            if (_comboInProgress) TickCombo();
            EvaluateBlockDecision();
        }

        private void TickAttackTank(float dist)
        {
            // Tank always closes in to melee range when in Attack state.
            // RollX/RollY animation driven by UpdateAnimatorMovement when _tankDetected.
            if (!_knockbackActive)
                TrySetDestination(_playerContext.Transform.position);

            FaceTarget(_playerContext.Transform.position);
            _attackCooldownTimer -= Time.deltaTime;

            if (dist <= _data.MeleeAttackRange && _attackCooldownTimer <= 0f && !_comboInProgress)
                StartCombo();

            if (_comboInProgress) TickCombo();
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
                _flankerOrbitSide *= -1f;
            }

            if (_comboInProgress) TickCombo();
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
                _animator?.SetTrigger(_animAttackHash);
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

        private void ExecuteSingleAttack()
        {
            if (_playerContext == null || _data?.CombatStats == null) return;

            float playerDefense = _playerContext.PlayerCombatStats?.Defense ?? 0f;
            float defEffectiveness = _playerContext.CombatSettings?.defenseEffectiveness ?? 0.5f;
            float finalDmg = Mathf.Max(0f, _scaledAttackStrength - playerDefense * defEffectiveness);

            if (_playerContext.CombatController?.IsSurgeActive ?? false)
            {
                float surgeDefMult = _playerContext.CombatSettings?.surgeDefenseMultiplier ?? 1.3f;
                finalDmg /= surgeDefMult;
            }

            _playerContext.Health.TakeDamage(finalDmg);
            _playerContext.Interaction?.NotifyDamageReceived();
        }

        private void EvaluateBlockDecision()
        {
            if (_isBlocking || _isStaggered) return;
            if (UnityEngine.Random.value < _data.BlockProbability * Time.deltaTime)
                StartBlock();
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
                    if (_data.Archetype == EnemyArchetype.Flanker)
                        awayDir = Vector3.Cross(awayDir, Vector3.up).normalized;
                    _evadeTarget = transform.position + awayDir * _data.EvasionDistance;
                }
                TransitionTo(EnemyState.Evade);
            }
        }

        #endregion

        #region Tank Animation Helper

        // Sets the Detected boolean on the animator and tracks the internal flag.
        private void SetTankDetected(bool detected)
        {
            if (_tankDetected == detected) return;
            _tankDetected = detected;
            _animator?.SetBool(_animDetectedHash, detected);
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
                if (_agent != null && !_knockbackActive) _agent.isStopped = false;
                OnStaggerChanged?.Invoke(false);
            }
        }

        private void TickPostureRegen()
        {
            if (_isStaggered || _currentPosture >= _data.ResolvedPostureMax) return;
            _postureRegenTimer += Time.deltaTime;
            if (_postureRegenTimer < 0.5f) return;
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

            if (_animator != null)
            {
                _animator.speed = 1f;
                _animator.SetTrigger(_animDefeatedHash);
            }

            if (_data?.DropData != null && _playerContext?.Resources != null)
            {
                if (_captiveCore != null && _captiveRevealed && _captiveCore.IsAvailable)
                    _captiveCore.ForceDestroy(_playerContext);
                else
                    _data.DropData.DeliverDestroyRewards(_playerContext.Resources);
            }

            if (_data?.IsElite ?? false)
                _playerContext?.EconomicEvents?.RollEnergyEventOnEliteEncounter();

            OnDefeated?.Invoke(this);
            _pendingDeactivation = true;
            _deactivationTimer = DeactivationDelay;
        }

        #endregion

        #region Patrol Helpers

        private void PickPatrolDestination()
        {
            const int MaxAttempts = 5;

            for (int i = 0; i < MaxAttempts; i++)
            {
                Vector2 rand2D = UnityEngine.Random.insideUnitCircle * _patrolRadius;
                Vector3 candidate = _patrolOrigin + new Vector3(rand2D.x, 0f, rand2D.y);

                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit,
                    _patrolNavMeshSampleRadius, NavMesh.AllAreas))
                {
                    _patrolDestination = hit.position;
                    if (_agent != null) _agent.isStopped = false;
                    return;
                }
            }

            _patrolDestination = _patrolOrigin;
            if (_agent != null) _agent.isStopped = false;
        }

        #endregion

        #region Utility

        private void TrySetDestination(Vector3 destination, bool force = false)
        {
            if (_agent == null || !_agent.isOnNavMesh) return;
            if (!force)
            {
                if (_destinationTimer < DestinationUpdateInterval) return;
                float moveSqr = (destination - _lastSubmittedDestination).sqrMagnitude;
                if (moveSqr < DestinationMoveThresholdSqr
                    && Vector3.Distance(transform.position, _lastSubmittedDestination)
                       <= _data.MeleeAttackRange) return;
            }
            _agent.SetDestination(destination);
            _lastSubmittedDestination = destination;
            _destinationTimer = 0f;
        }

        private void TransitionTo(EnemyState newState)
        {
            if (_state == newState) return;

            switch (_state)
            {
                case EnemyState.Search: _searchTimer = 0f; break;
                case EnemyState.Evade: _evadeTimer = 0f; break;
            }

            _state = newState;

            switch (_state)
            {
                case EnemyState.Patrol:
                    if (_agent != null) _agent.speed = _data.PatrolSpeed;
                    // Reset Detected on Tank when returning to passive patrol.
                    if (_data?.Archetype == EnemyArchetype.Tank && _tankDetected)
                        SetTankDetected(false);
                    _isPatrolWaiting = false;
                    _patrolMoveTimer = 0f;
                    PickPatrolDestination();
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
            transform.position += _knockbackVelocity * Time.deltaTime;
            _knockbackVelocity = Vector3.Lerp(_knockbackVelocity, Vector3.zero,
                KnockbackDecay * Time.deltaTime);

            if (_knockbackVelocity.sqrMagnitude < 0.01f)
            {
                _knockbackActive = false;
                _knockbackVelocity = Vector3.zero;
                if (_cc != null) _cc.enabled = true;
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
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(dir),
                _data.RotationSpeed * Time.deltaTime);
        }

        private void UpdateAnimatorMovement()
        {
            if (_animator == null || _agent == null) return;

            Vector3 velocity = _knockbackActive ? _knockbackVelocity : _agent.velocity;
            Vector3 localVelocity = transform.InverseTransformDirection(velocity);
            Vector2 normalized = Vector2.ClampMagnitude(
                new Vector2(localVelocity.x, localVelocity.z), 1f);

            if (_data?.Archetype == EnemyArchetype.Tank && _tankDetected)
            {
                // Once detected the tank animates through RollX/RollY.
                _animator.SetFloat(_animRollXHash, normalized.x, _animDamping, Time.deltaTime);
                _animator.SetFloat(_animRollYHash, normalized.y, _animDamping, Time.deltaTime);
                // Zero out the standard blend tree to avoid blending conflicts.
                _animator.SetFloat(_animMoveXHash, 0f, _animDamping, Time.deltaTime);
                _animator.SetFloat(_animMoveYHash, 0f, _animDamping, Time.deltaTime);
            }
            else
            {
                _animator.SetFloat(_animMoveXHash, normalized.x, _animDamping, Time.deltaTime);
                _animator.SetFloat(_animMoveYHash, normalized.y, _animDamping, Time.deltaTime);
            }
        }

        #endregion

        #region Public Query API

        public float NormalizedHP => _scaledMaxHP > 0f ? _currentHP / _scaledMaxHP : 0f;
        public float NormalizedPosture => _data?.ResolvedPostureMax > 0f
                                          ? _currentPosture / _data.ResolvedPostureMax : 0f;
        public string CurrentStateName => _state.ToString();
        public bool IsElite => _data != null && _data.IsElite;
        public bool IsNockdback => _knockbackActive;
        public Vector3 Destination => _lastSubmittedDestination;
        public float Distance => Vector3.Distance(transform.position, _lastSubmittedDestination);
        public SH_CombatStats CombatStats => _data?.CombatStats;

        public void SetPlayerContext(SH_PlayerContext ctx) => _playerContext = ctx;

        public void ResetEnemy(SH_PlayerContext playerContext)
        {
            if (_data == null) return;

            _playerContext = playerContext;
            _currentHP = _scaledMaxHP;
            _currentPosture = _data.ResolvedPostureMax;

            _captiveCore?.ResetCaptiveState();

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
            _isPatrolWaiting = false;
            _patrolMoveTimer = 0f;
            _patrolWaitTimer = 0f;

            // Reset tank detection state on respawn.
            if (_data?.Archetype == EnemyArchetype.Tank)
                SetTankDetected(false);

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

            Vector3 center = Application.isPlaying ? _patrolOrigin : transform.position;
            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.15f);
            Gizmos.DrawWireSphere(center, _patrolRadius);

            // Tank hold-distance ring.
            if (_data.Archetype == EnemyArchetype.Tank)
            {
                Gizmos.color = new Color(0.8f, 0.2f, 1f, 0.2f);
                Gizmos.DrawWireSphere(
                    Application.isPlaying ? transform.position : transform.position,
                    _data.DetectionRange * 0.5f);
            }
        }

        #endregion
    }
}