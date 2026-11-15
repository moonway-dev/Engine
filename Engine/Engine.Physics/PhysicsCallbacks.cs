using BepuPhysics;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Collidables;
using BepuUtilities;
using BepuUtilities.Memory;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Engine.Physics;

internal readonly struct NarrowPhaseCallbacks : INarrowPhaseCallbacks
{
    public void Initialize(Simulation simulation)
    {
    }

    public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin) => true;

    public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB) => true;

    public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties pairMaterial)
        where TManifold : unmanaged, IContactManifold<TManifold>
    {
        pairMaterial = default;
        pairMaterial.FrictionCoefficient = 0.7f;
        pairMaterial.MaximumRecoveryVelocity = 2f;
        pairMaterial.SpringSettings = new BepuPhysics.Constraints.SpringSettings(30f, 0.2f);
        return manifold.Count > 0;
    }

    public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB, ref ConvexContactManifold manifold) => manifold.Count > 0;

    public void Dispose()
    {
    }
}

internal struct PoseIntegratorCallbacks : IPoseIntegratorCallbacks
{
    public readonly Vector3 Gravity;
    public readonly float LinearDamping;
    public readonly float AngularDamping;

    private Vector3 _gravityDt;
    private float _linearDampingDt;
    private float _angularDampingDt;

    public AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.ConserveMomentum;

    public bool AllowSubstepsForUnconstrainedBodies => false;

    public bool IntegrateVelocityForKinematics => false;

    public PoseIntegratorCallbacks(Vector3 gravity, float linearDamping = 0.05f, float angularDamping = 0.05f)
    {
        Gravity = gravity;
        LinearDamping = linearDamping;
        AngularDamping = angularDamping;
    }

    public void Initialize(Simulation simulation)
    {
    }

    public void PrepareForIntegration(float dt)
    {
        _gravityDt = Gravity * dt;
        _linearDampingDt = MathF.Pow(MathF.Max(0f, 1f - LinearDamping), dt);
        _angularDampingDt = MathF.Pow(MathF.Max(0f, 1f - AngularDamping), dt);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IntegrateVelocity(
        Vector<int> bodyIndices,
        Vector3Wide position,
        QuaternionWide orientation,
        BodyInertiaWide localInertia,
        Vector<int> integrationMask,
        int workerIndex,
        Vector<float> dt,
        ref BodyVelocityWide velocity)
    {
        velocity.Linear += Vector3Wide.Broadcast(_gravityDt);
        velocity.Linear *= Vector<float>.One * _linearDampingDt;
        velocity.Angular *= Vector<float>.One * _angularDampingDt;
    }
}
