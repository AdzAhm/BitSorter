# BitSorter

2D Unity puzzle game. Bits (0/1) fall through logic components into bins.
Teaching digital logic: gates, flip-flops, adders, FSMs.

## Architecture rule (do not break this)
- Assets/Scripts/LogicCore/ = pure C#, NO UnityEngine imports.
  Deterministic tick-based simulator. Has its own asmdef.
- Assets/Scripts/View/ = Unity MonoBehaviours. Reads sim state, renders it.
- State flows LogicCore -> View only. The view never mutates the sim.
- No Rigidbody2D for bits. Physics is decoration only (sparks, debris).

## Conventions
- Every LogicCore component gets a unit test with its truth table.
- Levels are JSON in Assets/Resources/Levels/.

## My level
I'm a 3rd-semester CE student, new to Unity and git.
Explain things as you go and tell me when I'm about to do something dumb.
