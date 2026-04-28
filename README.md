# Scape Horizon

## Overview
**Scape Horizon** is an Action Roguelite with Hack and Slash gameplay featuring an isometric perspective. Developed in **Unity 6.3**, the project focuses on high-precision combat, fluid locomotion, and a distinct engineering-driven aesthetic. The game integrates deep narrative systems with responsive gameplay loops, centering on the protagonist, Luisa, and her interaction with advanced mecha technology.

## Core Features
* **Dynamic Combat & Locomotion:** Implementation of advanced Blend Trees for seamless movement transitions and responsive Input System handling.
* **Scanning & Targeting System:** A proprietary scanning mechanic that utilizes shader-based highlights and screen-space UI markers to identify interactable objects and threats.
* **Engineering Aesthetic:** A cohesive visual language utilizing the **Tektur** monospace font and high-contrast UI elements to simulate an advanced technical interface.
* **Tactical Interaction:** A robust interaction framework that supports interrupted states and immediate feedback loops for environmental engagement.

## Technical Specifications
* **Engine:** Unity 6.3 (Universal Render Pipeline).
* **Input:** New Unity Input System with event-driven subscriptions (Performed/Canceled patterns).
* **Rendering:** Unity Toon Shader (UTS3) for high-fidelity cel-shaded visuals.
* **UI Framework:** UI Toolkit (UXML/USS) for scalable, screen-space feedback and HUD management.
* **Architecture:** Modular C# implementation following industry-standard naming conventions (e.g., `SH_` prefix) and resource cleanup protocols to prevent memory leaks.

## Implementation Details (Development Progress)

### I. Locomotion & Physics
* Integrated a **Gravity System** with vertical velocity management.
* Implemented **Locomotion Blend Trees** adapted to current input actions for smooth mesh performance.
* Refactored movement logic to combine horizontal and vertical components using optimized vector operations (`sqrMagnitude`).

### II. Scanner & Targeting System (`SH_ScannerController`)
* **Scan Pulse:** Developed a sphere-mask based pulse that reveals targets within a specific radius.
* **Focused Targeting:** Implemented logic to identify the closest valid target to the crosshair using Dot Product calculations for precise selection.
* **Visual Feedback:** Integrated persistent highlights through custom shaders and transient pulse effects for enemies.

### III. UI Feedback & HUD (`SH_HUDController`)
* **Screen-Space Markers:** Added dynamic corner-bracket markers that track world-space targets and project them onto the HUD using `LateUpdate` loops.
* **Style Management:** CSS-like styling via USS for visibility states (`.marker-revealed` vs `.marker-hidden`).
* **Typography:** Standardized all technical readouts using the **Tektur** monospace font (GDD §9.1.2).

### IV. Interaction System
* Implemented `OnInteractionInterrupted` to handle proximity-based cancellations.
* Layer-based filtering to distinguish between persistent interactable markers and transient enemy highlights.

## Asset Pipeline
* **3D Modeling:** Managed in Blender.
* **Shaders:** Utilization of UTS3 for cross-pipeline compatibility (URP/HDRP).
* **Version Control:** Managed via Git with strict commit standards and resource disposal (`Dispose()`, `OnDisable()` unsubscriptions).

## Setup & Installation
1.  **Clone the repository:** `git clone https://github.com/UderoGames/Scape-Horizon.git`
2.  **Unity Version:** Open the project using **Unity 6.3.0f1** or higher.
3.  **Dependencies:** Ensure **Universal RP** and **UI Toolkit** packages are updated in the Package Manager.
4.  **Fonts:** Verify the `Tektur` font asset is correctly assigned in the Global Settings.

---
*Developed by Udero Games — Transforming knowledge and culture into sustainable interactive experiences.*
