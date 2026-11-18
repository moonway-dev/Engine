using System.Buffers.Binary;
using System.Text;
using Engine.RHI;

namespace Engine.Shaders;

public static class SpirVReflectionParser
{
    private const uint SpirVMagic = 0x07230203;
    private const int OpEntryPoint = 15;
    private const int OpName = 5;
    private const int OpDecorate = 71;
    private const int OpMemberDecorate = 72;
    private const int OpTypeVoid = 19;
    private const int OpTypeBool = 20;
    private const int OpTypeInt = 21;
    private const int OpTypeFloat = 22;
    private const int OpTypeVector = 23;
    private const int OpTypeMatrix = 24;
    private const int OpTypeImage = 25;
    private const int OpTypeSampler = 26;
    private const int OpTypeSampledImage = 27;
    private const int OpTypeArray = 28;
    private const int OpTypeRuntimeArray = 29;
    private const int OpTypeStruct = 30;
    private const int OpTypePointer = 32;
    private const int OpConstant = 43;
    private const int OpSpecConstant = 50;
    private const int OpVariable = 59;
    private const int OpTypeAccelerationStructureKHR = 5341;

    private const int DecorationBlock = 2;
    private const int DecorationBufferBlock = 3;
    private const int DecorationRowMajor = 4;
    private const int DecorationColMajor = 5;
    private const int DecorationArrayStride = 6;
    private const int DecorationMatrixStride = 7;
    private const int DecorationLocation = 30;
    private const int DecorationBinding = 33;
    private const int DecorationDescriptorSet = 34;
    private const int DecorationOffset = 35;

    private const int StorageUniformConstant = 0;
    private const int StorageInput = 1;
    private const int StorageUniform = 2;
    private const int StoragePushConstant = 9;
    private const int StorageStorageBuffer = 12;

    /// <summary>Resolve the actual emitted name, including Slang's default rename to main.</summary>
    public static string GetEntryPoint(byte[] spirV, ShaderStage stage, string? requestedName = null)
    {
        Module module = ReadModule(spirV);
        return SelectEntryPoint(module, stage, requestedName).Name;
    }

    public static PipelineReflection Parse(byte[] spirV, ShaderStage stage, string? entryPoint = null)
    {
        Module module = ReadModule(spirV);
        ShaderStages flags = StageToFlags(stage);
        EntryPointRecord? selected = module.EntryPoints.Count == 0 && entryPoint is null
            ? null : SelectEntryPoint(module, stage, entryPoint);
        List<PushConstantRange> pushConstants = [];
        List<uint> vertexInputs = [];
        Dictionary<uint, Dictionary<uint, DescriptorBinding>> sets = [];
        foreach (VariableRecord variable in module.Variables.Values)
        {
            // Since SPIR-V 1.4 every global used by an entry point is listed in its interface.
            if (selected is not null && module.Version >= 0x00010400 && !selected.Interface.Contains(variable.Id))
                continue;
            if (!module.Types.TryGetValue(variable.ResultTypeId, out TypeRecord? pointer) || pointer.Kind != TypeKind.Pointer)
                continue;
            if (!module.Types.TryGetValue(pointer.ComponentType, out TypeRecord? pointee))
                throw new ArgumentException($"Invalid SPIR-V: unresolved variable type {pointer.ComponentType}.", nameof(spirV));
            if (variable.StorageClass == StoragePushConstant)
            {
                uint offset = pointee.Kind == TypeKind.Struct && pointee.Members.Length > 0
                    ? pointee.MemberDecorations.Values.Where(d => d.Offset is not null).Select(d => d.Offset!.Value).DefaultIfEmpty(0u).Min() : 0;
                uint end = checked((uint)ComputeTypeSize(pointee, module));
                if (end > offset)
                    pushConstants.Add(new PushConstantRange { Offset = offset, Size = end - offset, Stages = flags });
                continue;
            }
            if (variable.StorageClass == StorageInput && stage == ShaderStage.Vertex && variable.Location is uint location)
            {
                vertexInputs.Add(location);
                continue;
            }
            if (variable.StorageClass is not (StorageUniform or StorageUniformConstant or StorageStorageBuffer))
                continue;
            if (variable.Binding is not uint binding)
                continue;

            (TypeRecord element, uint count) = UnwrapDescriptorArray(pointee, module);
            DescriptorType descriptorType = variable.StorageClass switch
            {
                StorageStorageBuffer when element.Kind == TypeKind.Struct => DescriptorType.StorageBuffer,
                StorageUniform when element.Kind == TypeKind.Struct => element.IsBufferBlock ? DescriptorType.StorageBuffer : DescriptorType.UniformBuffer,
                StorageUniformConstant => element.Kind switch
                {
                    TypeKind.AccelerationStructure => DescriptorType.AccelerationStructure,
                    TypeKind.Sampler => DescriptorType.Sampler,
                    TypeKind.SampledImage => DescriptorType.CombinedImageSampler,
                    TypeKind.Image when element.ImageDimension == 6 => DescriptorType.InputAttachment,
                    TypeKind.Image when element.ImageSampled == 2 => DescriptorType.StorageImage,
                    TypeKind.Image => DescriptorType.SampledImage,
                    _ => throw new NotSupportedException($"Unsupported descriptor element {element.Kind} at set {variable.DescriptorSet}, binding {binding}."),
                },
                _ => throw new NotSupportedException($"Unsupported descriptor storage/type at set {variable.DescriptorSet}, binding {binding}."),
            };
            uint setIndex = variable.DescriptorSet;
            if (!sets.TryGetValue(setIndex, out Dictionary<uint, DescriptorBinding>? bindings))
                sets[setIndex] = bindings = [];
            if (bindings.TryGetValue(binding, out DescriptorBinding? existing))
            {
                if (existing.Type != descriptorType || existing.ArrayCount != count)
                    throw new InvalidOperationException($"Incompatible descriptor set {setIndex}, binding {binding}: {existing.Type}[{existing.ArrayCount}] versus {descriptorType}[{count}].");
                bindings[binding] = existing with { Stages = existing.Stages | flags };
            }
            else
                bindings[binding] = new DescriptorBinding { Binding = binding, Type = descriptorType, ArrayCount = count, Stages = flags };
        }
        return new PipelineReflection
        {
            PushConstants = pushConstants.OrderBy(r => r.Offset).ToList(),
            DescriptorSets = sets.OrderBy(kvp => kvp.Key).Select(kvp => new DescriptorSetLayout
            {
                SetIndex = kvp.Key,
                Bindings = kvp.Value.OrderBy(b => b.Key).Select(b => b.Value).ToList(),
            }).ToList(),
            VertexInputs = vertexInputs.Distinct().Order().ToList(),
        };
    }

    private static EntryPointRecord SelectEntryPoint(Module module, ShaderStage stage, string? requestedName)
    {
        uint model = ExecutionModel(stage);
        List<EntryPointRecord> candidates = module.EntryPoints.Where(e => e.Model == model).ToList();
        if (requestedName is not null)
        {
            EntryPointRecord? exact = candidates.SingleOrDefault(e => e.Name == requestedName);
            if (exact is not null)
                return exact;
        }
        if (candidates.Count == 1)
            return candidates[0];
        throw new ArgumentException(candidates.Count == 0
            ? $"SPIR-V module has no entry point for {stage}."
            : $"SPIR-V module has multiple entry points for {stage}; specify an emitted entry point name.");
    }

    private static Module ReadModule(byte[] spirV)
    {
        ArgumentNullException.ThrowIfNull(spirV);
        if (spirV.Length < 20 || spirV.Length % 4 != 0 || ReadWord(spirV, 0) != SpirVMagic)
            throw new ArgumentException("Invalid SPIR-V module: bad header, magic number or byte length.", nameof(spirV));
        Module module = new() { Version = ReadWord(spirV, 1) };
        List<uint[]> decorations = [];
        List<uint[]> memberDecorations = [];
        int wordCount = spirV.Length / 4;
        for (int pointer = 5; pointer < wordCount;)
        {
            uint instruction = ReadWord(spirV, pointer);
            int count = (int)(instruction >> 16);
            int opcode = (int)(instruction & 0xffff);
            if (count == 0 || count > wordCount - pointer)
                throw new ArgumentException($"Invalid SPIR-V instruction length at word {pointer}.", nameof(spirV));
            uint[] words = new uint[count - 1];
            for (int i = 1; i < count; i++)
                words[i - 1] = ReadWord(spirV, pointer + i);
            pointer += count;
            int minimum = opcode switch
            {
                OpEntryPoint => 3,
                OpName => 2,
                OpDecorate => 2,
                OpMemberDecorate => 3,
                OpTypeVoid or OpTypeBool or OpTypeSampler or OpTypeStruct or OpTypeAccelerationStructureKHR => 1,
                OpTypeFloat or OpTypeSampledImage => 2,
                OpTypeInt or OpTypeVector or OpTypeMatrix or OpTypeArray or OpTypePointer or OpConstant or OpSpecConstant or OpVariable => 3,
                OpTypeRuntimeArray => 2,
                OpTypeImage => 8,
                _ => 0,
            };
            if (words.Length < minimum)
                throw new ArgumentException($"Invalid SPIR-V operands for opcode {opcode}.", nameof(spirV));
            switch (opcode)
            {
                case OpEntryPoint:
                    string name = ReadLiteralString(words, 2, out int interfaceStart);
                    module.EntryPoints.Add(new EntryPointRecord(words[0], name, words.Skip(interfaceStart).ToHashSet()));
                    break;
                case OpName:
                    break; // Resource names are not part of the RHI descriptor layout.
                case OpConstant:
                case OpSpecConstant:
                    module.Constants[words[1]] = new ConstantRecord(words[0], words.Skip(2).ToArray(), opcode == OpSpecConstant);
                    break;
                case OpDecorate:
                    decorations.Add(words);
                    break;
                case OpMemberDecorate:
                    memberDecorations.Add(words);
                    break;
                case OpTypeVoid:
                    module.Types[words[0]] = new TypeRecord(TypeKind.Void);
                    break;
                case OpTypeBool:
                    module.Types[words[0]] = new TypeRecord(TypeKind.Scalar, scalarWidth: 32);
                    break;
                case OpTypeInt:
                    module.Types[words[0]] = new TypeRecord(TypeKind.Scalar, scalarWidth: words[1], signed: words[2] != 0);
                    break;
                case OpTypeFloat:
                    module.Types[words[0]] = new TypeRecord(TypeKind.Scalar, scalarWidth: words[1]);
                    break;
                case OpTypeVector:
                    module.Types[words[0]] = new TypeRecord(TypeKind.Vector, componentType: words[1], componentCount: words[2]);
                    break;
                case OpTypeMatrix:
                    module.Types[words[0]] = new TypeRecord(TypeKind.Matrix, componentType: words[1], componentCount: words[2]);
                    break;
                case OpTypeImage:
                    module.Types[words[0]] = new TypeRecord(TypeKind.Image, imageSampled: words[6], imageDimension: words[2]);
                    break;
                case OpTypeSampler:
                    module.Types[words[0]] = new TypeRecord(TypeKind.Sampler);
                    break;
                case OpTypeSampledImage:
                    module.Types[words[0]] = new TypeRecord(TypeKind.SampledImage, componentType: words[1]);
                    break;
                case OpTypeAccelerationStructureKHR:
                    module.Types[words[0]] = new TypeRecord(TypeKind.AccelerationStructure);
                    break;
                case OpTypeArray:
                    module.Types[words[0]] = new TypeRecord(TypeKind.Array, componentType: words[1], arrayLengthId: words[2]);
                    break;
                case OpTypeRuntimeArray:
                    module.Types[words[0]] = new TypeRecord(TypeKind.RuntimeArray, componentType: words[1]);
                    break;
                case OpTypeStruct:
                    module.Types[words[0]] = new TypeRecord(TypeKind.Struct, members: words.Skip(1).ToArray());
                    break;
                case OpTypePointer:
                    module.Types[words[0]] = new TypeRecord(TypeKind.Pointer, componentType: words[2]);
                    break;
                case OpVariable:
                    module.Variables[words[1]] = new VariableRecord(words[1], words[0], words[2]);
                    break;
            }
        }
        foreach (uint[] words in decorations)
        {
            uint target = words[0];
            int decoration = (int)words[1];
            bool needsValue = decoration is DecorationBinding or DecorationDescriptorSet or DecorationLocation or DecorationArrayStride;
            if (needsValue && words.Length < 3)
                throw new ArgumentException("Invalid SPIR-V decoration operands.", nameof(spirV));
            if (module.Variables.TryGetValue(target, out VariableRecord? variable))
            {
                switch (decoration)
                {
                    case DecorationBinding: variable.Binding = words[2]; break;
                    case DecorationDescriptorSet: variable.DescriptorSet = words[2]; break;
                    case DecorationLocation: variable.Location = words[2]; break;
                }
            }
            if (module.Types.TryGetValue(target, out TypeRecord? type))
            {
                switch (decoration)
                {
                    case DecorationBlock: type.IsBlock = true; break;
                    case DecorationBufferBlock: type.IsBufferBlock = true; break;
                    case DecorationArrayStride: type.ArrayStride = words[2]; break;
                }
            }
        }
        foreach (uint[] words in memberDecorations)
        {
            if (!module.Types.TryGetValue(words[0], out TypeRecord? type))
                continue;
            int decoration = (int)words[2];
            if (decoration is DecorationOffset or DecorationMatrixStride && words.Length < 4)
                throw new ArgumentException("Invalid SPIR-V member decoration operands.", nameof(spirV));
            if (!type.MemberDecorations.TryGetValue(words[1], out MemberDecoration? member))
                type.MemberDecorations[words[1]] = member = new();
            switch (decoration)
            {
                case DecorationOffset: member.Offset = words[3]; break;
                case DecorationMatrixStride: member.MatrixStride = words[3]; break;
                case DecorationRowMajor: member.RowMajor = true; break;
                case DecorationColMajor: member.RowMajor = false; break;
            }
        }
        return module;
    }

    private static (TypeRecord Element, uint Count) UnwrapDescriptorArray(TypeRecord type, Module module)
    {
        uint count = 1;
        HashSet<TypeRecord> seen = [];
        while (type.Kind is TypeKind.Array or TypeKind.RuntimeArray)
        {
            if (!seen.Add(type))
                throw new ArgumentException("Invalid SPIR-V: cyclic descriptor array type.");
            if (type.Kind == TypeKind.RuntimeArray)
                throw new NotSupportedException("Runtime descriptor arrays require variable descriptor count support; fixed arrays are required.");
            count = checked(count * GetArrayCount(type, module));
            type = ResolveType(module, type.ComponentType);
        }
        return (type, count);
    }

    private static uint GetArrayCount(TypeRecord type, Module module)
    {
        if (!module.Constants.TryGetValue(type.ArrayLengthId, out ConstantRecord? constant))
            throw new ArgumentException($"Invalid SPIR-V: missing array length {type.ArrayLengthId}.");
        if (constant.Specialized)
            throw new NotSupportedException("Specialization-dependent descriptor/layout array lengths are not supported.");
        TypeRecord scalar = ResolveType(module, constant.TypeId);
        ulong value = constant.Words[0];
        if (scalar.ScalarWidth == 64)
        {
            if (constant.Words.Length < 2)
                throw new ArgumentException("Invalid SPIR-V: truncated 64-bit array length.");
            value |= (ulong)constant.Words[1] << 32;
        }
        if (value == 0 || value > uint.MaxValue || scalar.Signed && (value & (1ul << (int)(scalar.ScalarWidth - 1))) != 0)
            throw new NotSupportedException("Descriptor/layout array length must be a positive 32-bit count.");
        return (uint)value;
    }

    private static TypeRecord ResolveType(Module module, uint id) => module.Types.TryGetValue(id, out TypeRecord? type)
        ? type : throw new ArgumentException($"Invalid SPIR-V: missing type {id}.");

    private static ulong ComputeTypeSize(TypeRecord type, Module module, MemberDecoration? decoration = null)
    {
        switch (type.Kind)
        {
            case TypeKind.Scalar:
                return Math.Max(1u, type.ScalarWidth / 8);
            case TypeKind.Vector:
                return checked(type.ComponentCount * ComputeTypeSize(ResolveType(module, type.ComponentType), module));
            case TypeKind.Matrix:
            {
                TypeRecord column = ResolveType(module, type.ComponentType);
                uint vectors = decoration?.RowMajor == true ? column.ComponentCount : type.ComponentCount;
                ulong vectorSize = decoration?.RowMajor == true
                    ? type.ComponentCount * ComputeTypeSize(ResolveType(module, column.ComponentType), module)
                    : ComputeTypeSize(column, module);
                return checked((decoration?.MatrixStride ?? vectorSize) * vectors);
            }
            case TypeKind.Array:
                return checked((type.ArrayStride ?? ComputeTypeSize(ResolveType(module, type.ComponentType), module, decoration)) * GetArrayCount(type, module));
            case TypeKind.Struct:
            {
                ulong cursor = 0;
                for (uint i = 0; i < type.Members.Length; i++)
                {
                    TypeRecord member = ResolveType(module, type.Members[i]);
                    type.MemberDecorations.TryGetValue(i, out MemberDecoration? memberDecoration);
                    ulong alignment = ComputeTypeAlignment(member, module);
                    ulong offset = memberDecoration?.Offset ?? (cursor + alignment - 1) / alignment * alignment;
                    cursor = Math.Max(cursor, checked(offset + ComputeTypeSize(member, module, memberDecoration)));
                }
                return cursor;
            }
            default:
                throw new NotSupportedException($"Unsupported push constant type {type.Kind}.");
        }
    }

    private static ulong ComputeTypeAlignment(TypeRecord type, Module module) => type.Kind switch
    {
        TypeKind.Scalar => Math.Max(1u, type.ScalarWidth / 8),
        TypeKind.Vector => (type.ComponentCount == 3 ? 4 : type.ComponentCount) * ComputeTypeAlignment(ResolveType(module, type.ComponentType), module),
        TypeKind.Matrix or TypeKind.Array => ComputeTypeAlignment(ResolveType(module, type.ComponentType), module),
        TypeKind.Struct => type.Members.Select(id => ComputeTypeAlignment(ResolveType(module, id), module)).DefaultIfEmpty(1ul).Max(),
        _ => 1,
    };

    private static ShaderStages StageToFlags(ShaderStage stage) => stage switch
    {
        ShaderStage.Vertex => ShaderStages.Vertex,
        ShaderStage.Fragment => ShaderStages.Fragment,
        ShaderStage.Compute => ShaderStages.Compute,
        ShaderStage.Geometry => ShaderStages.Geometry,
        ShaderStage.TessellationControl => ShaderStages.TessellationControl,
        ShaderStage.TessellationEvaluation => ShaderStages.TessellationEvaluation,
        ShaderStage.RayGeneration => ShaderStages.RayGeneration,
        ShaderStage.Miss => ShaderStages.Miss,
        ShaderStage.ClosestHit => ShaderStages.ClosestHit,
        ShaderStage.AnyHit => ShaderStages.AnyHit,
        ShaderStage.Intersection => ShaderStages.Intersection,
        ShaderStage.Callable => ShaderStages.Callable,
        _ => throw new NotSupportedException($"Unsupported shader stage {stage}."),
    };

    private static uint ExecutionModel(ShaderStage stage) => stage switch
    {
        ShaderStage.Vertex => 0,
        ShaderStage.TessellationControl => 1,
        ShaderStage.TessellationEvaluation => 2,
        ShaderStage.Geometry => 3,
        ShaderStage.Fragment => 4,
        ShaderStage.Compute => 5,
        ShaderStage.RayGeneration => 5313,
        ShaderStage.Intersection => 5314,
        ShaderStage.AnyHit => 5315,
        ShaderStage.ClosestHit => 5316,
        ShaderStage.Miss => 5317,
        ShaderStage.Callable => 5318,
        _ => throw new NotSupportedException($"Unsupported shader stage {stage}."),
    };

    private static uint ReadWord(byte[] data, int index) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(index * 4, 4));

    private static string ReadLiteralString(uint[] words, int startIndex, out int nextIndex)
    {
        List<byte> bytes = [];
        for (int i = startIndex; i < words.Length; i++)
        {
            for (int b = 0; b < 4; b++)
            {
                byte value = (byte)(words[i] >> (b * 8));
                if (value == 0)
                {
                    nextIndex = i + 1;
                    return Encoding.UTF8.GetString(bytes.ToArray());
                }
                bytes.Add(value);
            }
        }
        throw new ArgumentException("Invalid SPIR-V: unterminated entry point name.");
    }

    private enum TypeKind { Void, Scalar, Vector, Matrix, Image, Sampler, SampledImage, AccelerationStructure, Array, RuntimeArray, Struct, Pointer }

    private sealed class Module
    {
        public uint Version { get; init; }
        public Dictionary<uint, TypeRecord> Types { get; } = [];
        public Dictionary<uint, VariableRecord> Variables { get; } = [];
        public Dictionary<uint, ConstantRecord> Constants { get; } = [];
        public List<EntryPointRecord> EntryPoints { get; } = [];
    }

    private sealed record EntryPointRecord(uint Model, string Name, HashSet<uint> Interface);
    private sealed record ConstantRecord(uint TypeId, uint[] Words, bool Specialized);

    private sealed class TypeRecord(TypeKind kind, uint componentType = 0, uint componentCount = 0,
        uint scalarWidth = 0, bool signed = false, uint arrayLengthId = 0, uint imageSampled = 0,
        uint imageDimension = 0, uint[]? members = null)
    {
        public TypeKind Kind { get; } = kind;
        public uint ComponentType { get; } = componentType;
        public uint ComponentCount { get; } = componentCount;
        public uint ScalarWidth { get; } = scalarWidth;
        public bool Signed { get; } = signed;
        public uint ArrayLengthId { get; } = arrayLengthId;
        public uint ImageSampled { get; } = imageSampled;
        public uint ImageDimension { get; } = imageDimension;
        public uint[] Members { get; } = members ?? [];
        public bool IsBlock { get; set; }
        public bool IsBufferBlock { get; set; }
        public ulong? ArrayStride { get; set; }
        public Dictionary<uint, MemberDecoration> MemberDecorations { get; } = [];
    }

    private sealed class MemberDecoration
    {
        public uint? Offset { get; set; }
        public ulong? MatrixStride { get; set; }
        public bool RowMajor { get; set; }
    }

    private sealed class VariableRecord(uint id, uint resultTypeId, uint storageClass)
    {
        public uint Id { get; } = id;
        public uint ResultTypeId { get; } = resultTypeId;
        public uint StorageClass { get; } = storageClass;
        public uint DescriptorSet { get; set; }
        public uint? Binding { get; set; }
        public uint? Location { get; set; }
    }
}
