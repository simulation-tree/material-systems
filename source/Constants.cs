using System;

namespace Materials.Systems
{
    internal static class Constants
    {
        public static readonly BlendFactor[] blendFactors = Enum.GetValues<BlendFactor>();
        public static readonly BlendOperation[] blendOperations = Enum.GetValues<BlendOperation>();
        public static readonly CompareOperation[] compareOperations = Enum.GetValues<CompareOperation>();
        public static readonly StencilOperation[] stencilOperations = Enum.GetValues<StencilOperation>();
        public static readonly string[] blendFactorOptions;
        public static readonly string[] blendOperationOptions;
        public static readonly string[] compareOperationOptions;
        public static readonly string[] stencilOperationOptions;

        static Constants()
        {
            blendFactorOptions = new string[blendFactors.Length];
            for (int i = 0; i < blendFactors.Length; i++)
            {
                blendFactorOptions[i] = blendFactors[i].ToString();
            }

            blendOperationOptions = new string[blendOperations.Length];
            for (int i = 0; i < blendOperations.Length; i++)
            {
                blendOperationOptions[i] = blendOperations[i].ToString();
            }

            compareOperationOptions = new string[compareOperations.Length];
            for (int i = 0; i < compareOperations.Length; i++)
            {
                compareOperationOptions[i] = compareOperations[i].ToString();
            }

            stencilOperationOptions = new string[stencilOperations.Length];
            for (int i = 0; i < stencilOperations.Length; i++)
            {
                stencilOperationOptions[i] = stencilOperations[i].ToString();
            }
        }

        public static BlendFactor? GetBlendFactor(ReadOnlySpan<char> text)
        {
            for (int i = 0; i < blendFactorOptions.Length; i++)
            {
                if (text.Equals(blendFactorOptions[i], StringComparison.OrdinalIgnoreCase))
                {
                    return blendFactors[i];
                }
            }

            return null;
        }

        public static BlendOperation? GetBlendOperation(ReadOnlySpan<char> text)
        {
            for (int i = 0; i < blendOperationOptions.Length; i++)
            {
                if (text.Equals(blendOperationOptions[i], StringComparison.OrdinalIgnoreCase))
                {
                    return blendOperations[i];
                }
            }

            return null;
        }

        public static CompareOperation? GetCompareOperation(ReadOnlySpan<char> text)
        {
            for (int i = 0; i < compareOperationOptions.Length; i++)
            {
                if (text.Equals(compareOperationOptions[i], StringComparison.OrdinalIgnoreCase))
                {
                    return compareOperations[i];
                }
            }

            return null;
        }

        public static StencilOperation? GetStencilOperation(ReadOnlySpan<char> text)
        {
            for (int i = 0; i < stencilOperationOptions.Length; i++)
            {
                if (text.Equals(stencilOperationOptions[i], StringComparison.OrdinalIgnoreCase))
                {
                    return stencilOperations[i];
                }
            }

            return null;
        }
    }
}