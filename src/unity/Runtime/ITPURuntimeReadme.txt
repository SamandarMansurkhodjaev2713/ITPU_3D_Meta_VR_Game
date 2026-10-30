STAGING SOURCE ONLY — no Unity compilation or on-device test has been performed.

Use tools/stage_unity_sources.py only in an explicitly approved phase. It copies the canonical domain source and Unity adapters into a NEW staging tree. Do not copy .csproj into Unity Assets.

Adapters intentionally do not assume a version-specific Meta class name. Bind actual Interaction SDK events after the P1 capability probe. There is no second rig, hand provider or competing grab implementation in these files.

FixedNpcStation expects separate, explicitly assigned aim pivots. Animator clips must not write those pivots. The stationary root includes no NavMeshAgent, locomotion animation, applyRootMotion or physics-driven translation. Validate root stability, feet, clip blending and head limitations in Unity and Quest.

CoffeePresenter models one identified reusable cup. A multi-cup café system is out of scope. Dock events must identify that actual cup and be deduplicated by the SDK adapter; generic colliders must not act as the cup. Liquid surface uses Unity local Y after validated import.

SessionHost must be wired to actual headset/tracking lifecycle events, not just desktop focus. Reset callbacks must remove their own transient state; reset_fault pauses on subscriber exception and needs operator repair.

The wrappers are not a full game and do not implement complete scene assembly, audio mixing, facial animation, XR inputs, board drawing or APK release.
