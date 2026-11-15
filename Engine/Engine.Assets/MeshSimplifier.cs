using Engine.Mathematics;
using Engine.Rendering;

namespace Engine.Assets;

public sealed record MeshLodData(Vertex[] Vertices, uint[] Indices, float Error);

public static class MeshSimplifier
{
    private struct Quadric
    {
        public double XX, XY, XZ, XW, YY, YZ, YW, ZZ, ZW, WW;

        public void Add(Vector3 normal, double d)
        {
            double x = normal.X, y = normal.Y, z = normal.Z;
            XX += x * x; XY += x * y; XZ += x * z; XW += x * d;
            YY += y * y; YZ += y * z; YW += y * d;
            ZZ += z * z; ZW += z * d; WW += d * d;
        }

        public readonly double Evaluate(Vector3 p) => Math.Max(0,
            XX * p.X * p.X + 2 * XY * p.X * p.Y + 2 * XZ * p.X * p.Z + 2 * XW * p.X
            + YY * p.Y * p.Y + 2 * YZ * p.Y * p.Z + 2 * YW * p.Y
            + ZZ * p.Z * p.Z + 2 * ZW * p.Z + WW);
    }

    public static MeshLodData Simplify(Vertex[] sourceVertices, uint[] sourceIndices, float ratio,
        long memoryBudget = 1536L * 1024 * 1024, CancellationToken cancellationToken = default,
        TimeSpan? timeBudget = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!(ratio > 0 && ratio < 1))
            throw new ArgumentOutOfRangeException(nameof(ratio));
        if (sourceIndices.Length % 3 != 0)
            throw new ArgumentException("Triangle index count must be divisible by three.", nameof(sourceIndices));
        long estimate = checked((long)sourceVertices.Length * 240 + (long)sourceIndices.Length * 24);
        if (estimate > memoryBudget)
            throw new InvalidOperationException("LOD preprocessing exceeds its memory budget.");
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        TimeSpan limit = timeBudget ?? TimeSpan.FromMinutes(2);
        int target = Math.Max(4, (int)(sourceIndices.Length / 3 * ratio));
        Vertex[] vertices = sourceVertices;
        uint[] indices = sourceIndices;
        float error = 0;
        for (int pass = 0; pass < 48 && indices.Length / 3 > target; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (System.Diagnostics.Stopwatch.GetElapsedTime(started) > limit)
                break;
            int[] offsets = new int[vertices.Length + 1];
            for (int i = 0; i < indices.Length; i++)
            {
                if ((i & 16383) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                uint index = indices[i];
                if (index >= vertices.Length)
                    throw new InvalidDataException("Invalid simplification index.");
                offsets[index + 1]++;
            }
            for (int i = 1; i < offsets.Length; i++)
                offsets[i] += offsets[i - 1];
            int[] cursor = (int[])offsets.Clone();
            int[] adjacency = new int[indices.Length];
            Quadric[] quadrics = new Quadric[vertices.Length];
            for (int i = 0; i < indices.Length; i += 3)
            {
                if ((i & 16383) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                int a = (int)indices[i], b = (int)indices[i + 1], c = (int)indices[i + 2];
                adjacency[cursor[a]++] = i;
                adjacency[cursor[b]++] = i;
                adjacency[cursor[c]++] = i;
                Vector3 normal = Vector3.Cross(vertices[b].Position - vertices[a].Position, vertices[c].Position - vertices[a].Position);
                if (normal.LengthSquared <= 1e-30f)
                    continue;
                normal = normal.Normalized;
                double d = -Vector3.Dot(normal, vertices[a].Position);
                quadrics[a].Add(normal, d);
                quadrics[b].Add(normal, d);
                quadrics[c].Add(normal, d);
            }
            bool[] locked = new bool[vertices.Length];
            for (int a = 0; a < vertices.Length; a++)
            {
                if ((a & 4095) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                if (offsets[a + 1] - offsets[a] > 64)
                {
                    locked[a] = true;
                    continue;
                }
                for (int j = offsets[a]; j < offsets[a + 1] && !locked[a]; j++)
                {
                    int triangle = adjacency[j];
                    for (int k = 0; k < 3; k++)
                    {
                        int b = (int)indices[triangle + k];
                        if (b != a && EdgeFaces(a, b, offsets, adjacency, indices) != 2)
                        {
                            locked[a] = true;
                            break;
                        }
                    }
                }
            }
            int[] remap = new int[vertices.Length];
            float[] radii = new float[vertices.Length];
            Array.Fill(remap, -1);
            bool[] touched = new bool[vertices.Length];
            int removed = 0;
            for (int a = 0; a < vertices.Length && (indices.Length / 3 - removed) > target; a++)
            {
                if ((a & 4095) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                if (locked[a] || touched[a])
                    continue;
                int best = -1;
                double cost = double.PositiveInfinity;
                for (int j = offsets[a]; j < offsets[a + 1]; j++)
                {
                    int triangle = adjacency[j];
                    for (int k = 0; k < 3; k++)
                    {
                        int b = (int)indices[triangle + k];
                        if (a == b || locked[b] || touched[b]
                            || vertices[a].UV != vertices[b].UV
                            || Vector3.Dot(vertices[a].Normal, vertices[b].Normal) < 0)
                            continue;
                        double candidate = quadrics[a].Evaluate(vertices[b].Position) + quadrics[b].Evaluate(vertices[b].Position);
                        if (candidate < cost && CanCollapse(a, b, vertices, indices, offsets, adjacency))
                        {
                            cost = candidate;
                            best = b;
                        }
                    }
                }
                if (best < 0)
                    continue;
                remap[a] = best;
                radii[best] = Math.Max(radii[best], (vertices[a].Position - vertices[best].Position).Length);
                MarkRing(a, touched, indices, offsets, adjacency);
                MarkRing(best, touched, indices, offsets, adjacency);
                removed += 2;
            }
            if (removed == 0)
                break;
            float passError = 0;
            foreach (float radius in radii)
                passError = Math.Max(passError, radius);
            error += passError;
            List<uint> output = new(indices.Length - removed * 3);
            for (int i = 0; i < indices.Length; i += 3)
            {
                if ((i & 16383) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                uint a = Map(indices[i]), b = Map(indices[i + 1]), c = Map(indices[i + 2]);
                if (a == b || a == c || b == c)
                    continue;
                output.Add(a); output.Add(b); output.Add(c);
            }
            Array.Fill(remap, -1);
            List<Vertex> compact = [];
            for (int i = 0; i < output.Count; i++)
            {
                int index = (int)output[i];
                if (remap[index] < 0)
                {
                    remap[index] = compact.Count;
                    compact.Add(vertices[index]);
                }
                output[i] = (uint)remap[index];
            }
            vertices = [.. compact];
            indices = [.. output];

            uint Map(uint index) => remap[index] < 0 ? index : (uint)remap[index];
        }
        return new MeshLodData(vertices, indices, error);
    }

    private static int EdgeFaces(int a, int b, int[] offsets, int[] adjacency, uint[] indices)
    {
        int count = 0;
        for (int j = offsets[a]; j < offsets[a + 1]; j++)
        {
            int t = adjacency[j];
            if (indices[t] == b || indices[t + 1] == b || indices[t + 2] == b)
                count++;
        }
        return count;
    }

    private static bool CanCollapse(int a, int b, Vertex[] vertices, uint[] indices, int[] offsets, int[] adjacency)
    {
        if (EdgeFaces(a, b, offsets, adjacency, indices) != 2)
            return false;
        int common = 0;
        Span<int> neighbors = stackalloc int[192];
        int count = 0;
        for (int j = offsets[a]; j < offsets[a + 1]; j++)
        {
            int t = adjacency[j];
            for (int k = 0; k < 3; k++)
            {
                int n = (int)indices[t + k];
                if (n == a || n == b || neighbors[..count].Contains(n))
                    continue;
                if (count == neighbors.Length)
                    return false;
                neighbors[count++] = n;
                if (EdgeFaces(b, n, offsets, adjacency, indices) > 0)
                    common++;
            }
            int x = (int)indices[t], y = (int)indices[t + 1], z = (int)indices[t + 2];
            if (x == b || y == b || z == b)
                continue;
            Vector3 oldNormal = Vector3.Cross(vertices[y].Position - vertices[x].Position, vertices[z].Position - vertices[x].Position);
            Vector3 p = x == a ? vertices[b].Position : vertices[x].Position;
            Vector3 q = y == a ? vertices[b].Position : vertices[y].Position;
            Vector3 r = z == a ? vertices[b].Position : vertices[z].Position;
            Vector3 nextNormal = Vector3.Cross(q - p, r - p);
            if (nextNormal.LengthSquared <= 1e-30f || Vector3.Dot(oldNormal, nextNormal) <= 0
                || Vector3.Dot(oldNormal, nextNormal) * Vector3.Dot(oldNormal, nextNormal)
                    < 0.04f * oldNormal.LengthSquared * nextNormal.LengthSquared)
                return false;
        }
        if (common != 2)
            return false;
        for (int j = offsets[a]; j < offsets[a + 1]; j++)
        {
            int t = adjacency[j];
            if (indices[t] == b || indices[t + 1] == b || indices[t + 2] == b)
                continue;
            int x = (int)indices[t] == a ? (int)indices[t + 1] : (int)indices[t];
            int y = (int)indices[t + 2] == a ? (int)indices[t + 1] : (int)indices[t + 2];
            for (int k = offsets[b]; k < offsets[b + 1]; k++)
            {
                int other = adjacency[k];
                bool hasX = indices[other] == x || indices[other + 1] == x || indices[other + 2] == x;
                bool hasY = indices[other] == y || indices[other + 1] == y || indices[other + 2] == y;
                if (hasX && hasY)
                    return false;
            }
        }
        return true;
    }

    private static void MarkRing(int a, bool[] touched, uint[] indices, int[] offsets, int[] adjacency)
    {
        touched[a] = true;
        for (int j = offsets[a]; j < offsets[a + 1]; j++)
        {
            int t = adjacency[j];
            touched[indices[t]] = true;
            touched[indices[t + 1]] = true;
            touched[indices[t + 2]] = true;
        }
    }
}
