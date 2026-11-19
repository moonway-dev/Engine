using BepuPhysics;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Collidables;
using BepuUtilities;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Engine.Physics;

internal readonly struct NarrowPhaseCallbacks(PhysicsBodySettings settings) : INarrowPhaseCallbacks
{
    public void Initialize(Simulation simulation)
    {
    }

    public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin) =>
        settings.AllowsCollision(a) && settings.AllowsCollision(b);

    public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB) =>
        settings.AllowsCollision(pair.A) && settings.AllowsCollision(pair.B);

    public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties pairMaterial)
        where TManifold : unmanaged, IContactManifold<TManifold>
    {
        pairMaterial = default;
        pairMaterial.FrictionCoefficient = 0.7f;
        pairMaterial.MaximumRecoveryVelocity = 2f;
        pairMaterial.SpringSettings = new BepuPhysics.Constraints.SpringSettings(120f, 1f);
        return manifold.Count > 0 && settings.AllowsCollision(pair.A) && settings.AllowsCollision(pair.B);
    }

    public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB, ref ConvexContactManifold manifold) =>
        manifold.Count > 0 && settings.AllowsCollision(pair.A) && settings.AllowsCollision(pair.B);

    public void Dispose()
    {
    }
}

internal struct PoseIntegratorCallbacks(PhysicsBodySettings settings) : IPoseIntegratorCallbacks
{
    private Simulation _simulation = null!;

    public AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.ConserveMomentum;
    public bool AllowSubstepsForUnconstrainedBodies => false;
    public bool IntegrateVelocityForKinematics => false;

    public void Initialize(Simulation simulation) => _simulation = simulation;

    public void PrepareForIntegration(float dt)
    {
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
        Span<float> linearX = stackalloc float[Vector<float>.Count];
        Span<float> linearY = stackalloc float[Vector<float>.Count];
        Span<float> linearZ = stackalloc float[Vector<float>.Count];
        Span<float> angularX = stackalloc float[Vector<float>.Count];
        Span<float> angularY = stackalloc float[Vector<float>.Count];
        Span<float> angularZ = stackalloc float[Vector<float>.Count];
        for (int lane = 0; lane < Vector<float>.Count; lane++)
        {
            linearX[lane] = velocity.Linear.X[lane];
            linearY[lane] = velocity.Linear.Y[lane];
            linearZ[lane] = velocity.Linear.Z[lane];
            angularX[lane] = velocity.Angular.X[lane];
            angularY[lane] = velocity.Angular.Y[lane];
            angularZ[lane] = velocity.Angular.Z[lane];
            if (integrationMask[lane] == 0 || bodyIndices[lane] < 0)
                continue;
            BodyHandle handle = _simulation.Bodies.ActiveSet.IndexToHandle[bodyIndices[lane]];
            RigidbodySettings body = settings.Bodies.TryGetValue(handle, out PhysicsBodyState? state) ? state.Settings : RigidbodySettings.Default;
            float timestep = dt[lane];
            float linearDamping = MathF.Exp(-body.LinearDamping * timestep);
            float angularDamping = MathF.Exp(-body.AngularDamping * timestep);
            Vector3 gravity = body.UseGravity ? settings.Gravity * timestep : Vector3.Zero;
            linearX[lane] = (body.Constraints & RigidbodyConstraints.FreezePositionX) != 0 ? 0f : (linearX[lane] + gravity.X) * linearDamping;
            linearY[lane] = (body.Constraints & RigidbodyConstraints.FreezePositionY) != 0 ? 0f : (linearY[lane] + gravity.Y) * linearDamping;
            linearZ[lane] = (body.Constraints & RigidbodyConstraints.FreezePositionZ) != 0 ? 0f : (linearZ[lane] + gravity.Z) * linearDamping;
            System.Numerics.Quaternion bodyRotation = new(
                orientation.X[lane], orientation.Y[lane], orientation.Z[lane], orientation.W[lane]);
            System.Numerics.Quaternion inverseRotation = System.Numerics.Quaternion.Inverse(bodyRotation);
            System.Numerics.Vector3 angularWorld = new(angularX[lane], angularY[lane], angularZ[lane]);
            System.Numerics.Vector3 angularLocal = System.Numerics.Vector3.Transform(angularWorld, inverseRotation);
            angularLocal.X = (body.Constraints & RigidbodyConstraints.FreezeRotationX) != 0 ? 0f : angularLocal.X * angularDamping;
            angularLocal.Y = (body.Constraints & RigidbodyConstraints.FreezeRotationY) != 0 ? 0f : angularLocal.Y * angularDamping;
            angularLocal.Z = (body.Constraints & RigidbodyConstraints.FreezeRotationZ) != 0 ? 0f : angularLocal.Z * angularDamping;
            System.Numerics.Vector3 filteredAngular = System.Numerics.Vector3.Transform(angularLocal, bodyRotation);
            angularX[lane] = filteredAngular.X;
            angularY[lane] = filteredAngular.Y;
            angularZ[lane] = filteredAngular.Z;
        }
        velocity.Linear.X = new Vector<float>(linearX);
        velocity.Linear.Y = new Vector<float>(linearY);
        velocity.Linear.Z = new Vector<float>(linearZ);
        velocity.Angular.X = new Vector<float>(angularX);
        velocity.Angular.Y = new Vector<float>(angularY);
        velocity.Angular.Z = new Vector<float>(angularZ);
    }
}
