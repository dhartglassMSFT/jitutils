// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Antigen.Tree;

namespace Antigen.Expressions
{
    public class ConstantValue : Expression
    {
        public readonly string Value;

        private static readonly Dictionary<string, List<string>> s_vectorConstants = new Dictionary<string, List<string>>()
        {
            { "Vector2", new List<string>() { "One", "Zero", "UnitX", "UnitY" } },
            { "Vector3", new List<string>() { "One", "Zero", "UnitX", "UnitY", "UnitZ" } },
            { "Vector4", new List<string>() { "One", "Zero", "UnitW", "UnitX", "UnitY", "UnitZ" } },
        };

        // AllBitsSet on a floating-point element type is a NaN (every bit set). NaN is absorbing:
        // it propagates through arithmetic, so seeding a computation with one collapses everything
        // downstream to NaN and the generated test stops discriminating between a correct and an
        // incorrect JIT. Measured over a 6-step op chain, a NaN seed produces 1 distinct state out
        // of 6, versus 5 of 6 for One/Indices. It is also what makes Debug/Release NaN
        // representation differences observable at all. So keep generating it, but rarely.
        //
        // Integral element types are unaffected: there AllBitsSet is -1 / MaxValue, which is a
        // valuable fuzzing input, so they retain the original rate.
        private const double FloatingPointAllBitsSetProbability = 0.05;
        private const double IntegralAllBitsSetProbability = 0.5;

        // Constants that are well-defined and non-absorbing for every element type and every
        // vector width. All are available in net9.0 and later.
        private static readonly List<string> s_nonAbsorbingVectorConstants = new List<string>() { "Zero", "One", "Indices" };

        protected ConstantValue(Tree.ValueType valueType, string value) : base(null)
        {
            if (valueType.PrimitiveType == Primitive.Char)
            {
                Value = $"'{value}'";
                return;
            }
            else if (valueType.PrimitiveType == Primitive.String)
            {
                Value = $"\"{value}\"";
                return;
            }
            else if (valueType.PrimitiveType == Primitive.Float)
            {
                Value = $"{value}f";
                return;
            }
            else if (valueType.PrimitiveType == Primitive.Decimal)
            {
                Value = $"{value}m";
                return;
            }
            else if ((valueType.PrimitiveType & Primitive.UnsignedInteger) != 0)
            {
                value = value.TrimStart('-');
            }
            else if (valueType.PrimitiveType == Primitive.Boolean)
            {
                Debug.Assert(value == "false" || value == "true");
            }
            Value = value;
        }

        public override string ToString()
        {
            return $"{Value}";
        }

        public static ConstantValue GetRandomConstantInt(int min, int max)
        {
            return new ConstantValue(Tree.ValueType.ForPrimitive(Primitive.Int), PRNG.Next(min, max).ToString());
        }

        /// <summary>
        ///     Return ConstantValue for `value`.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static ConstantValue GetConstantValue(int value)
        {
            return new ConstantValue(Tree.ValueType.ForPrimitive(Primitive.Int), value.ToString());
        }

        public static ConstantValue GetConstantValue(Tree.ValueType literalType, IList<Weights<int>> numerals)
        {
            string constantValue;
            if (literalType.IsVectorType)
            {
                constantValue = literalType.ToString();

                if (literalType.IsVectorNumerics())
                {
                    var vectorConstantProps = s_vectorConstants[literalType.ToString()];
                    constantValue += ("." + vectorConstantProps[PRNG.Next(vectorConstantProps.Count)]);
                }
                else
                {
                    double allBitsSetProbability = literalType.HasFloatingPointElement()
                        ? FloatingPointAllBitsSetProbability
                        : IntegralAllBitsSetProbability;

                    constantValue += PRNG.Decide(allBitsSetProbability)
                        ? ".AllBitsSet"
                        : ("." + s_nonAbsorbingVectorConstants[PRNG.Next(s_nonAbsorbingVectorConstants.Count)]);
                }
            }
            else if ((literalType.PrimitiveType & Primitive.Numeric) != 0)
            {
                // numeric
                int literalValue = PRNG.WeightedChoice(numerals);
                constantValue = literalValue.ToString();

                // If unsigned, and number selected is negative, then flip it
                if ((literalType.PrimitiveType & Primitive.UnsignedInteger) != 0)
                {
                    if (literalValue < 0)
                    {
                        if (literalValue == int.MinValue)
                        {
                            literalValue = int.MaxValue;
                        }
                        else
                        {
                            literalValue *= -1;
                        }
                    }
                }

                switch (literalType.PrimitiveType)
                {
                    case Tree.Primitive.Byte:
                        constantValue = ((byte)(literalValue % byte.MaxValue)).ToString();
                        break;
                    case Tree.Primitive.SByte:
                        constantValue = ((sbyte)(literalValue % sbyte.MaxValue)).ToString();
                        break;
                    case Tree.Primitive.UShort:
                        constantValue = ((ushort)(literalValue % ushort.MaxValue)).ToString();
                        break;
                    case Tree.Primitive.Short:
                        constantValue = ((short)(literalValue % short.MaxValue)).ToString();
                        break;
                    case Tree.Primitive.Int:
                    case Tree.Primitive.UInt:
                    case Tree.Primitive.Long:
                    case Tree.Primitive.ULong:
                        // already constantValue is populated
                        break;
                    case Tree.Primitive.Float:
                        constantValue = ((float)literalValue + (float)PRNG.Next(5) / PRNG.Next(10, 100)).ToString();
                        break;
                    case Tree.Primitive.Decimal:
                        constantValue = ((decimal)literalValue + (decimal)PRNG.Next(5) / PRNG.Next(10, 100)).ToString();
                        break;
                    case Tree.Primitive.Double:
                        constantValue = ((double)literalValue + (double)PRNG.Next(5) / PRNG.Next(10, 100)).ToString();
                        break;
                    // case Tree.Primitive.SveMaskPattern:
                    //     constantValue = (Helpers.GetRandomByte() % 16).ToString();
                    //     break;
                    default:
                        Debug.Assert(false, string.Format("Hit unknown value type {0}", Enum.GetName(typeof(Tree.Primitive), literalType.PrimitiveType)));
                        constantValue = "1";
                        break;
                }
            }
            else
            {
                // non-numeric
                switch (literalType.PrimitiveType)
                {
                    case Tree.Primitive.Boolean:
                        constantValue = PRNG.Decide(0.5) ? "true" : "false";
                        break;

                    case Tree.Primitive.Char:
                        constantValue = Helpers.GetRandomChar().ToString();
                        break;

                    case Tree.Primitive.String:
                        constantValue = Helpers.GetRandomString();
                        break;
                    case Tree.Primitive.SveMaskPattern:
                        constantValue = Helpers.GetRandomEnumValue(literalType.PrimitiveType);
                        break;
                    default:
                        Debug.Assert(false, string.Format("Hit unknown value type {0}", Enum.GetName(typeof(Tree.Primitive), literalType.PrimitiveType)));
                        constantValue = "1";

                        break;
                }
            }

            return new ConstantValue(literalType, constantValue);
        }
    }
}
