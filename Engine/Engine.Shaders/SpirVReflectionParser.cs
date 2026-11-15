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
    private const int OpTypeInt = 21;
    private const int OpTypeFloat = 22;
    private const int OpTypeVector = 23;
    private const int OpTypeMatrix = 24;
    private const int OpTypeImage = 25;
    private const int OpTypeSampler = 26;
    private const int OpTypeSampledImage = 27;
    private const int OpTypeArray = 28;
    private const int OpTypeStruct = 30;
    private const int OpTypePointer = 32;
    private const int OpConstant = 43;
    private const int OpVariable = 59;

    private const int DecorationBlock = 2;
    private const int DecorationLocation = 30;
    private const int DecorationDescriptorSet = 34;
    private const int DecorationBinding = 33;
    private const int DecorationOffset = 35;
    private const int DecorationArrayStride = 6;
    private const int DecorationMatrixStride = 7;
    private const int DecorationColMajor = 5;

    private const int StorageUniformConstant = 0;
    private const int StorageInput = 1;
    private const int StorageUniform = 2;
    private const int StoragePushConstant = 9;
    private const int StorageStorageBuffer = 12;

    public static PipelineReflection Parse(byte[] spirV, ShaderStage stage)
    {
        if (spirV.Length < 20 || ReadWord(spirV, 0) != SpirVMagic)
            throw new ArgumentException("Invalid SPIR-V module: bad magic number.", nameof(spirV));

        uint wordCount = (uint)(spirV.Length / 4);
        Dictionary<uint, uint[]> instructions = [];
        Dictionary<uint, string> names = [];
        Dictionary<uint, TypeRecord> types = [];
        Dictionary<uint, VariableRecord> variables = [];
        Dictionary<uint, uint> constants = [];
        List<uint> entryPointIds = [];
        List<uint[]> pendingDecorations = [];
        List<uint[]> pendingMemberDecorations = [];

        uint pointer = 5;
        while (pointer < wordCount)
        {
            uint instruction = ReadWord(spirV, pointer);
            uint count = instruction >> 16;
            int opcode = (int)(instruction & 0xFFFF);
            if (count == 0)
                break;
            uint[] words = new uint[count - 1];
            for (uint i = 1; i < count && pointer + i < wordCount; i++)
                words[i - 1] = ReadWord(spirV, pointer + i);
            pointer += count;

            switch (opcode)
            {
                case OpEntryPoint:
                    entryPointIds.Add(words.Length > 1 ? words[1] : 0);
                    break;
                case OpName:
                    names[words[0]] = ReadLiteralString(words, 1);
                    break;
                case OpConstant:
                    constants[words[1]] = words.Length > 2 ? words[2] : 0;
                    break;
                case OpDecorate:
                    pendingDecorations.Add(words);
                    break;
                case OpMemberDecorate:
                    pendingMemberDecorations.Add(words);
                    break;
                case OpTypeVoid:
                    types[words[0]] = new TypeRecord(TypeKind.Void);
                    break;
                case OpTypeInt:
                    types[words[0]] = new TypeRecord(TypeKind.Scalar, scalarWidth: words.Length > 2 ? words[2] : 32);
                    break;
                case OpTypeFloat:
                    types[words[0]] = new TypeRecord(TypeKind.Scalar, scalarWidth: words.Length > 1 ? words[1] : 32);
                    break;
                case OpTypeVector:
                    types[words[0]] = new TypeRecord(TypeKind.Vector, componentType: words[1], componentCount: words.Length > 2 ? words[2] : 4);
                    break;
                case OpTypeMatrix:
                    types[words[0]] = new TypeRecord(TypeKind.Matrix, componentType: words[1], componentCount: words.Length > 2 ? words[2] : 4);
                    break;
                case OpTypeImage:
                    types[words[0]] = new TypeRecord(TypeKind.Image, imageSampled: words.Length > 6 ? words[6] : 1);
                    break;
                case OpTypeSampler:
                    types[words[0]] = new TypeRecord(TypeKind.Sampler);
                    break;
                case OpTypeSampledImage:
                    types[words[0]] = new TypeRecord(TypeKind.SampledImage, componentType: words[1]);
                    break;
                case OpTypeArray:
                    types[words[0]] = new TypeRecord(TypeKind.Array, componentType: words[1], arrayLengthId: words.Length > 2 ? words[2] : 0);
                    break;
                case OpTypeStruct:
                    types[words[0]] = new TypeRecord(TypeKind.Struct, members: words.Skip(1).ToArray());
                    break;
                case OpTypePointer:
                    types[words[0]] = new TypeRecord(TypeKind.Pointer, storageClass: words[1], componentType: words[2]);
                    break;
                case OpVariable:
                    types[words[1]] = new TypeRecord(TypeKind.Variable, componentType: words[0], storageClass: words.Length > 2 ? words[2] : 0);
                    variables[words[1]] = new VariableRecord(words[1], words[0], words.Length > 2 ? words[2] : 0);
                    break;
            }
        }

        ApplyDecorations(pendingDecorations, pendingMemberDecorations, names, types, variables, constants);
        return BuildReflection(stage, names, types, variables, constants);
    }

    private static void ApplyDecorations(
        List<uint[]> decorations,
        List<uint[]> memberDecorations,
        Dictionary<uint, string> names,
        Dictionary<uint, TypeRecord> types,
        Dictionary<uint, VariableRecord> variables,
        Dictionary<uint, uint> constants)
    {
        foreach (uint[] words in memberDecorations)
            ParseMemberDecorate(types, words);
        foreach (uint[] words in decorations)
            ParseDecorate(names, types, variables, words);
    }

    private static void ParseDecorate(
        Dictionary<uint, string> names,
        Dictionary<uint, TypeRecord> types,
        Dictionary<uint, VariableRecord> variables,
        uint[] words)
    {
        uint target = words[0];
        int decoration = (int)words[1];
        if (decoration == DecorationDescriptorSet && variables.TryGetValue(target, out VariableRecord? variable))
        {
            variable.DescriptorSet = words[2];
        }
        else if (decoration == DecorationLocation && variables.ContainsKey(target))
        {
            variables[target].Location = words[2];
        }
        else if (decoration == DecorationBinding && variables.TryGetValue(target, out VariableRecord? bindingVariable))
        {
            bindingVariable.Binding = words[2];
        }
        else if ((decoration == DecorationBlock || decoration == DecorationOffset || decoration == DecorationArrayStride || decoration == DecorationMatrixStride)
                 && types.TryGetValue(target, out TypeRecord? decoratedType))
        {
            ApplyTypeDecoration(decoratedType, decoration, words);
        }
        else if (decoration == DecorationBlock
                 && variables.ContainsKey(target)
                 && types.TryGetValue(ResolveVariableType(types, variables, target), out TypeRecord? variableType)
                 && variableType is not null)
        {
            variableType.IsBlock = true;
        }
        else if (decoration == DecorationArrayStride && types.TryGetValue(target, out TypeRecord? arrayType))
        {
            arrayType.ArrayStride = words[2];
        }
    }

    private static void ApplyTypeDecoration(TypeRecord type, int decoration, uint[] words)
    {
        switch (decoration)
        {
            case DecorationBlock:
                type.IsBlock = true;
                break;
            case DecorationOffset:
                if (type.MemberOffsets.Count > 0)
                    type.MemberOffsets[^1] = words[2];
                break;
            case DecorationArrayStride:
                type.ArrayStride = words[2];
                break;
            case DecorationMatrixStride:
                type.MatrixStride = words[2];
                break;
        }
    }

    private static void ParseMemberDecorate(Dictionary<uint, TypeRecord> types, uint[] words)
    {
        uint structTypeId = words[0];
        if (!types.TryGetValue(structTypeId, out TypeRecord? type))
            return;
        uint memberIndex = words[1];
        int decoration = (int)words[2];
        while (type.MemberOffsets.Count <= memberIndex)
            type.MemberOffsets.Add(0);
        switch (decoration)
        {
            case DecorationOffset:
                type.MemberOffsets[(int)memberIndex] = words[3];
                break;
            case DecorationArrayStride:
                type.ArrayStride = words[3];
                break;
            case DecorationMatrixStride:
                type.MatrixStride = words[3];
                break;
        }
    }

    private static uint ResolveVariableType(Dictionary<uint, TypeRecord> types, Dictionary<uint, VariableRecord> variables, uint variableId)
    {
        if (!variables.TryGetValue(variableId, out VariableRecord? variable))
            return 0;
        if (types.TryGetValue(variable.ResultTypeId, out TypeRecord? pointer) && pointer.Kind == TypeKind.Pointer)
            return pointer.ComponentType;
        return 0;
    }

    private static PipelineReflection BuildReflection(
        ShaderStage stage,
        Dictionary<uint, string> names,
        Dictionary<uint, TypeRecord> types,
        Dictionary<uint, VariableRecord> variables,
        Dictionary<uint, uint> constants)
    {
        List<PushConstantRange> pushConstants = [];
        List<uint> vertexInputs = [];
        Dictionary<uint, DescriptorSetLayoutBuilder> setBuilders = [];

        foreach (VariableRecord variable in variables.Values)
        {
            if (!types.TryGetValue(variable.ResultTypeId, out TypeRecord? pointer) || pointer.Kind != TypeKind.Pointer)
                continue;
            if (!types.TryGetValue(pointer.ComponentType, out TypeRecord? pointee))
                continue;
            string name = names.TryGetValue(variable.Id, out string? variableName) ? variableName : $"resource{variable.Id}";

            switch (pointer.StorageClass)
            {
                case StoragePushConstant:
                {
                    ulong size = ComputeStructSize(pointee, types, constants);
                    pushConstants.Add(new PushConstantRange { Offset = 0, Size = (uint)size, Stages = StageToFlags(stage) });
                    break;
                }
                case StorageUniform:
                {
                    DescriptorType kind = pointee.Kind switch
                    {
                        TypeKind.Image => DescriptorType.StorageImage,
                        TypeKind.Sampler => DescriptorType.Sampler,
                        TypeKind.SampledImage => DescriptorType.CombinedImageSampler,
                        _ => DescriptorType.UniformBuffer,
                    };
                    uint arrayCount = pointee.Kind == TypeKind.Array ? (uint)GetArrayCount(pointee, constants) : 1;
                    GetBuilder(setBuilders, variable.DescriptorSet).Add(variable.Binding, kind, StageToFlags(stage), arrayCount, name);
                    break;
                }
                case StorageUniformConstant:
                {
                    DescriptorType kind = pointee.Kind switch
                    {
                        TypeKind.Sampler => DescriptorType.Sampler,
                        TypeKind.SampledImage => DescriptorType.CombinedImageSampler,
                        TypeKind.Image when pointee.ImageSampled == 2 => DescriptorType.StorageImage,
                        TypeKind.Image => DescriptorType.SampledImage,
                        _ => DescriptorType.SampledImage,
                    };
                    uint arrayCount = pointee.Kind == TypeKind.Array ? (uint)GetArrayCount(pointee, constants) : 1;
                    GetBuilder(setBuilders, variable.DescriptorSet).Add(variable.Binding, kind, StageToFlags(stage), arrayCount, name);
                    break;
                }
                case StorageStorageBuffer:
                    GetBuilder(setBuilders, variable.DescriptorSet).Add(variable.Binding, DescriptorType.StorageBuffer, StageToFlags(stage), 1, name);
                    break;
                case StorageInput when stage == ShaderStage.Vertex && variable.Location is uint location:
                    vertexInputs.Add(location);
                    break;
            }
        }

        List<DescriptorSetLayout> sets = setBuilders
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => kvp.Value.Build(kvp.Key))
            .ToList();

        List<PushConstantRange> merged = [];
        foreach (PushConstantRange range in pushConstants)
        {
            PushConstantRange? existing = merged.FirstOrDefault(r => r.Offset == range.Offset);
            if (existing is not null)
                merged.Remove(existing);
            ShaderStages stages = existing is null ? range.Stages : existing.Stages | range.Stages;
            uint size = existing is null ? range.Size : Math.Max(existing.Size, range.Size);
            merged.Add(new PushConstantRange { Offset = range.Offset, Size = size, Stages = stages });
        }

        return new PipelineReflection { PushConstants = merged, DescriptorSets = sets, VertexInputs = vertexInputs };
    }

    private static DescriptorSetLayoutBuilder GetBuilder(Dictionary<uint, DescriptorSetLayoutBuilder> builders, uint setIndex)
    {
        if (!builders.TryGetValue(setIndex, out DescriptorSetLayoutBuilder? builder))
        {
            builder = new DescriptorSetLayoutBuilder();
            builders[setIndex] = builder;
        }
        return builder;
    }

    private static ShaderStages StageToFlags(ShaderStage stage) => stage switch
    {
        ShaderStage.Vertex => ShaderStages.Vertex,
        ShaderStage.Fragment => ShaderStages.Fragment,
        ShaderStage.Compute => ShaderStages.Compute,
        ShaderStage.Geometry => ShaderStages.Geometry,
        ShaderStage.TessellationControl => ShaderStages.TessellationControl,
        ShaderStage.TessellationEvaluation => ShaderStages.TessellationEvaluation,
        _ => ShaderStages.None,
    };

    private static ulong GetArrayCount(TypeRecord arrayType, Dictionary<uint, uint> constants) =>
        constants.TryGetValue(arrayType.ArrayLengthId, out uint count) ? count : 1;

    private static ulong ComputeStructSize(TypeRecord structType, Dictionary<uint, TypeRecord> types, Dictionary<uint, uint> constants)
    {
        if (structType.Kind != TypeKind.Struct)
            return ComputeTypeSize(structType, types, constants);
        ulong cursor = 0;
        for (int i = 0; i < structType.Members.Length; i++)
        {
            if (!types.TryGetValue(structType.Members[i], out TypeRecord? member))
                continue;
            ulong memberSize = ComputeTypeSize(member, types, constants);
            ulong memberAlignment = ComputeTypeAlignment(member, types);
            ulong offset = i < structType.MemberOffsets.Count
                ? structType.MemberOffsets[i]
                : (cursor + memberAlignment - 1) / memberAlignment * memberAlignment;
            cursor = Math.Max(cursor, offset + memberSize);
        }
        return cursor;
    }

    private static ulong ComputeTypeAlignment(TypeRecord type, Dictionary<uint, TypeRecord> types) => type.Kind switch
    {
        TypeKind.Scalar => Math.Max(1ul, type.ScalarWidth / 8),
        TypeKind.Vector => (ulong)(type.ComponentCount >= 3 ? 16 : type.ComponentCount * (types.TryGetValue(type.ComponentType, out TypeRecord? component) && component is not null ? component.ScalarWidth : 32) / 8),
        TypeKind.Matrix => 16,
        TypeKind.Array => types.TryGetValue(type.ComponentType, out TypeRecord? element) ? ComputeTypeAlignment(element, types) : 16,
        TypeKind.Struct => 16,
        _ => 1,
    };

    private static ulong ComputeTypeSize(TypeRecord type, Dictionary<uint, TypeRecord> types, Dictionary<uint, uint> constants) => type.Kind switch
    {
        TypeKind.Scalar => type.ScalarWidth / 8,
        TypeKind.Vector => (ulong)(type.ComponentCount * (types.TryGetValue(type.ComponentType, out TypeRecord? component) && component is not null ? component.ScalarWidth : 32) / 8),
        TypeKind.Matrix => (ulong)(type.ComponentCount * 4 * 4),
        TypeKind.Array => type.ArrayStride > 0 ? type.ArrayStride * GetArrayCount(type, constants) : 0,
        TypeKind.Struct => ComputeStructSize(type, types, constants),
        _ => 0,
    };

    private static uint ReadWord(byte[] data, uint index) =>
        BitConverter.ToUInt32(data, (int)(index * 4));

    private static string ReadLiteralString(uint[] words, int startIndex)
    {
        List<byte> bytes = [];
        for (int i = startIndex; i < words.Length; i++)
        {
            uint word = words[i];
            for (int b = 0; b < 4; b++)
            {
                byte value = (byte)((word >> (b * 8)) & 0xFF);
                if (value == 0)
                    return System.Text.Encoding.UTF8.GetString([.. bytes]);
                bytes.Add(value);
            }
        }
        return System.Text.Encoding.UTF8.GetString([.. bytes]);
    }

    private enum TypeKind
    {
        Void,
        Scalar,
        Vector,
        Matrix,
        Image,
        Sampler,
        SampledImage,
        Array,
        Struct,
        Pointer,
        Variable,
    }

    private sealed class TypeRecord(
        TypeKind kind,
        uint componentType = 0,
        uint componentCount = 0,
        uint scalarWidth = 0,
        uint storageClass = 0,
        uint arrayLengthId = 0,
        uint imageSampled = 0,
        uint[]? members = null)
    {
        public TypeKind Kind { get; } = kind;
        public uint ComponentType { get; } = componentType;
        public uint ComponentCount { get; } = componentCount;
        public uint ScalarWidth { get; } = scalarWidth;
        public uint StorageClass { get; } = storageClass;
        public uint ArrayLengthId { get; } = arrayLengthId;
        public uint ImageSampled { get; } = imageSampled;
        public uint[] Members { get; } = members ?? [];
        public bool IsBlock { get; set; }
        public ulong ArrayStride { get; set; }
        public ulong MatrixStride { get; set; }
        public List<uint> MemberOffsets { get; } = [];
    }

    private sealed class VariableRecord(uint id, uint resultTypeId, uint storageClass)
    {
        public uint Id { get; } = id;
        public uint ResultTypeId { get; } = resultTypeId;
        public uint StorageClass { get; } = storageClass;
        public uint DescriptorSet { get; set; }
        public uint Binding { get; set; }
        public uint? Location { get; set; }
    }

    private sealed class DescriptorSetLayoutBuilder
    {
        private readonly Dictionary<uint, DescriptorBinding> _bindings = [];

        public void Add(uint binding, DescriptorType type, ShaderStages stages, uint arrayCount, string name)
        {
            if (_bindings.TryGetValue(binding, out DescriptorBinding? existing))
            {
                _bindings[binding] = existing with { Stages = existing.Stages | stages };
                return;
            }
            _bindings[binding] = new DescriptorBinding
            {
                Binding = binding,
                Type = type,
                Stages = stages,
                ArrayCount = Math.Max(1u, arrayCount),
            };
        }

        public DescriptorSetLayout Build(uint setIndex) => new()
        {
            SetIndex = setIndex,
            Bindings = _bindings.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value).ToList(),
        };
    }
}
