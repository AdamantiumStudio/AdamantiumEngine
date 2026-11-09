using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Adamantium.Fonts.Common;
using Adamantium.Fonts.Tables.CFF;

namespace Adamantium.Fonts.Parsers.CFF
{
    internal class DictOperandParser
    {
        private Dictionary<ushort, DictOperatorsType> byteToOperatorMap;
        private Dictionary<DictOperatorsType, Func<List<GenericOperandResult>, GenericOperandResult>> operatorsActions;
        protected Dictionary<DictOperatorsType, List<GenericOperandResult>> OperatorsRawValues;
        protected CFFFont Font { get; }

        public DictOperandParser(byte[] rawData, CFFFont font)
        {
            OperatorsRawValues = new Dictionary<DictOperatorsType, List<GenericOperandResult>>();
            Font = font;
            
            byteToOperatorMap = new Dictionary<ushort, DictOperatorsType>
            {
                { (ushort)DictOperatorsType.version              , DictOperatorsType.version              },
                { (ushort)DictOperatorsType.Notice               , DictOperatorsType.Notice               },
                { (ushort)DictOperatorsType.Copyright            , DictOperatorsType.Copyright            },
                { (ushort)DictOperatorsType.FullName             , DictOperatorsType.FullName             },
                { (ushort)DictOperatorsType.FamilyName           , DictOperatorsType.FamilyName           },
                { (ushort)DictOperatorsType.Weight               , DictOperatorsType.Weight               },
                { (ushort)DictOperatorsType.isFixedPitch         , DictOperatorsType.isFixedPitch         },
                { (ushort)DictOperatorsType.ItalicAngle          , DictOperatorsType.ItalicAngle          },
                { (ushort)DictOperatorsType.UnderlinePosition    , DictOperatorsType.UnderlinePosition    },
                { (ushort)DictOperatorsType.UnderlineThickness   , DictOperatorsType.UnderlineThickness   },
                { (ushort)DictOperatorsType.PaintType            , DictOperatorsType.PaintType            },
                { (ushort)DictOperatorsType.CharStringType       , DictOperatorsType.CharStringType       },
                { (ushort)DictOperatorsType.FontMatrix           , DictOperatorsType.FontMatrix           },
                { (ushort)DictOperatorsType.UniqueID             , DictOperatorsType.UniqueID             },
                { (ushort)DictOperatorsType.FontBBox             , DictOperatorsType.FontBBox             },
                { (ushort)DictOperatorsType.StrokeWidth          , DictOperatorsType.StrokeWidth          },
                { (ushort)DictOperatorsType.XUID                 , DictOperatorsType.XUID                 },
                { (ushort)DictOperatorsType.charset              , DictOperatorsType.charset              },
                { (ushort)DictOperatorsType.Encoding             , DictOperatorsType.Encoding             },
                { (ushort)DictOperatorsType.CharStrings          , DictOperatorsType.CharStrings          },
                { (ushort)DictOperatorsType.Private              , DictOperatorsType.Private              },
                { (ushort)DictOperatorsType.SyntheticBase        , DictOperatorsType.SyntheticBase        },
                { (ushort)DictOperatorsType.PostScript           , DictOperatorsType.PostScript           },
                { (ushort)DictOperatorsType.BaseFontName         , DictOperatorsType.BaseFontName         },
                { (ushort)DictOperatorsType.BaseFontBlend        , DictOperatorsType.BaseFontBlend        },
                { (ushort)DictOperatorsType.ROS                  , DictOperatorsType.ROS                  },
                { (ushort)DictOperatorsType.CIDFontVersion       , DictOperatorsType.CIDFontVersion       },
                { (ushort)DictOperatorsType.CIDFontRevision      , DictOperatorsType.CIDFontRevision      },
                { (ushort)DictOperatorsType.CIDFontType          , DictOperatorsType.CIDFontType          },
                { (ushort)DictOperatorsType.CIDCount             , DictOperatorsType.CIDCount             },
                { (ushort)DictOperatorsType.UIDBase              , DictOperatorsType.UIDBase              },
                { (ushort)DictOperatorsType.FDArray              , DictOperatorsType.FDArray              },
                { (ushort)DictOperatorsType.FDSelect             , DictOperatorsType.FDSelect             },
                { (ushort)DictOperatorsType.FontName             , DictOperatorsType.FontName             },
                
                // Private DICT
                { (ushort)DictOperatorsType.BlueValues           , DictOperatorsType.BlueValues          },
                { (ushort)DictOperatorsType.OtherBlues           , DictOperatorsType.OtherBlues          },
                { (ushort)DictOperatorsType.FamilyBlues          , DictOperatorsType.FamilyBlues         },
                { (ushort)DictOperatorsType.FamilyOtherBlues     , DictOperatorsType.FamilyOtherBlues    },
                { (ushort)DictOperatorsType.BlueScale            , DictOperatorsType.BlueScale           },
                { (ushort)DictOperatorsType.BlueShift            , DictOperatorsType.BlueShift           },
                { (ushort)DictOperatorsType.BlueFuzz             , DictOperatorsType.BlueFuzz            },
                { (ushort)DictOperatorsType.StdHW                , DictOperatorsType.StdHW               },
                { (ushort)DictOperatorsType.StdVW                , DictOperatorsType.StdVW               },
                { (ushort)DictOperatorsType.StemSnapH            , DictOperatorsType.StemSnapH           },
                { (ushort)DictOperatorsType.StemSnapV            , DictOperatorsType.StemSnapV           },
                { (ushort)DictOperatorsType.ForceBold            , DictOperatorsType.ForceBold           },
                { (ushort)DictOperatorsType.LanguageGroup        , DictOperatorsType.LanguageGroup       },
                { (ushort)DictOperatorsType.ExpansionFactor      , DictOperatorsType.ExpansionFactor     },
                { (ushort)DictOperatorsType.InitialRandomSeed    , DictOperatorsType.InitialRandomSeed   },
                { (ushort)DictOperatorsType.Subrs                , DictOperatorsType.Subrs               },
                { (ushort)DictOperatorsType.DefaultWidthX        , DictOperatorsType.DefaultWidthX       },
                { (ushort)DictOperatorsType.NominalWidthX        , DictOperatorsType.NominalWidthX       },
                
                // CFF 2
                { (ushort)DictOperatorsType.vsindex              , DictOperatorsType.vsindex             },
                { (ushort)DictOperatorsType.blend                , DictOperatorsType.blend               },
                { (ushort)DictOperatorsType.vstore               , DictOperatorsType.vstore              }
            };

            operatorsActions = new Dictionary<DictOperatorsType, Func<List<GenericOperandResult>, GenericOperandResult>>
            {
               { DictOperatorsType.version                , Sid },
               { DictOperatorsType.Notice                 , Sid },
               { DictOperatorsType.Copyright              , Sid },
               { DictOperatorsType.FullName               , Sid },
               { DictOperatorsType.FamilyName             , Sid },
               { DictOperatorsType.Weight                 , Sid },
               { DictOperatorsType.isFixedPitch           , Boolean },
               { DictOperatorsType.ItalicAngle            , Number },
               { DictOperatorsType.UnderlinePosition      , Number },
               { DictOperatorsType.UnderlineThickness     , Number },
               { DictOperatorsType.PaintType              , Number },
               { DictOperatorsType.CharStringType         , Number },
               { DictOperatorsType.FontMatrix             , NumberArray },
               { DictOperatorsType.UniqueID               , Number },
               { DictOperatorsType.FontBBox               , NumberArray },
               { DictOperatorsType.StrokeWidth            , Number },
               { DictOperatorsType.XUID                   , NumberArray },
               { DictOperatorsType.charset                , Number },
               { DictOperatorsType.Encoding               , Number },
               { DictOperatorsType.CharStrings            , Number },
               { DictOperatorsType.Private                , NumberNumber },
               { DictOperatorsType.SyntheticBase          , Number },
               { DictOperatorsType.PostScript             , Sid },
               { DictOperatorsType.BaseFontName           , Sid },
               { DictOperatorsType.BaseFontBlend          , Delta },
               { DictOperatorsType.ROS                    , SidSidNumber },
               { DictOperatorsType.CIDFontVersion         , Number },
               { DictOperatorsType.CIDFontRevision        , Number },
               { DictOperatorsType.CIDFontType            , Number },
               { DictOperatorsType.CIDCount               , Number },
               { DictOperatorsType.UIDBase                , Number },
               { DictOperatorsType.FDArray                , Number },
               { DictOperatorsType.FDSelect               , Number },
               { DictOperatorsType.FontName               , Sid },
               
               // Private DICT part
               { DictOperatorsType.BlueValues            , Delta },
               { DictOperatorsType.OtherBlues            , Delta },
               { DictOperatorsType.FamilyBlues           , Delta },
               { DictOperatorsType.FamilyOtherBlues      , Delta },
               { DictOperatorsType.BlueScale             , Number },
               { DictOperatorsType.BlueShift             , Number },
               { DictOperatorsType.BlueFuzz              , Number },
               { DictOperatorsType.StdHW                 , Number },
               { DictOperatorsType.StdVW                 , Number },
               { DictOperatorsType.StemSnapH             , Delta },
               { DictOperatorsType.StemSnapV             , Delta },
               { DictOperatorsType.ForceBold             , Boolean },
               { DictOperatorsType.LanguageGroup         , Number },
               { DictOperatorsType.ExpansionFactor       , Number },
               { DictOperatorsType.InitialRandomSeed     , Number },
               { DictOperatorsType.Subrs                 , Number },
               { DictOperatorsType.DefaultWidthX         , Number },
               { DictOperatorsType.NominalWidthX         , Number },
               
               // CFF 2
               { DictOperatorsType.vsindex               , Number },
               { DictOperatorsType.blend                 , Delta  },
               { DictOperatorsType.vstore                , Number }
            };

            FillDefaultValues();
            FillOperatorsRawValues(font, rawData);
        }
        
        protected GenericOperandResult Number(CffDictReader reader)
        {
            return reader.Peek() == 30 ? Double(reader) : Integer(reader);
        }

        protected GenericOperandResult Integer(CffDictReader reader)
        {
            var b0 = reader.Read();

            if (b0 >= 32 && b0 <= 246)
            {
                return b0 - 139;
            }
            else if (b0 >= 247 && b0 <= 250)
            {
                var b1 = reader.Read();

                return (b0 - 247) * 256 + b1 + 108;
            }
            else if (b0 >= 251 && b0 <= 254)
            {
                var b1 = reader.Read();

                return -(b0 - 251) * 256 - b1 - 108;
            }
            else if (b0 == 28)
            {
                var b1 = reader.Read();
                var b2 = reader.Read();

                return (short)((b1 << 8) | b2);
            }
            else if (b0 == 29)
            {
                var b1 = reader.Read();
                var b2 = reader.Read();
                var b3 = reader.Read();
                var b4 = reader.Read();

                return (b1 << 24) | (b2 << 16) | (b3 << 8) | b4;
            }
            else
            {
                throw new InvalidDataException($"A CFF DICT holds byte {b0}, which is neither operand nor operator.");
            }
        }

        protected GenericOperandResult Double(CffDictReader reader)
        {
            var text = new StringBuilder();
            reader.Read();

            var done = false;
            while (!done)
            {
                var ns = ParseNibbles(reader.Read());
                foreach (var nibble in new[] { ns.HiNibble, ns.LoNibble })
                {
                    if (!GetTokenFromNibble(nibble, out var token))
                    {
                        done = true;
                        break;
                    }

                    text.Append(token);
                }
            }

            if (!double.TryParse(text.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                double.IsInfinity(value))
            {
                throw new InvalidDataException($"A CFF DICT holds a real number that is not one: '{text}'.");
            }

            return value;
        }

        // convert raw Numbers to each operator's output type (e.g. calculate Deltas)
        private static void Require(List<GenericOperandResult> rawOperands, int count)
        {
            if (rawOperands.Count < count)
            {
                throw new InvalidDataException("A CFF DICT operator lacks its operands.");
            }
        }

        protected GenericOperandResult Number(List<GenericOperandResult> rawOperands)
        {
            Require(rawOperands, 1);
            return rawOperands[0];
        }

        protected GenericOperandResult NumberNumber(List<GenericOperandResult> rawOperands)
        {
            Require(rawOperands, 2);
            var number1 = rawOperands[0];
            var number2 = rawOperands[1];

            return (Number1: number1.AsDouble(), Number2: number2.AsDouble());
        }

        protected GenericOperandResult Boolean(List<GenericOperandResult> rawOperands)
        {
            Require(rawOperands, 1);
            return rawOperands[0];
        }

        protected GenericOperandResult Sid(List<GenericOperandResult> rawOperands)
        {
            Require(rawOperands, 1);
            return rawOperands[0];
        }

        protected GenericOperandResult SidSidNumber(List<GenericOperandResult> rawOperands)
        {
            Require(rawOperands, 3);
            ushort sid1 = rawOperands[0].AsUShort();
            ushort sid2 = rawOperands[1].AsUShort();
            double number = rawOperands[2].AsDouble();

            return (Sid1: sid1, Sid2: sid2, Number: number);
        }
        protected GenericOperandResult NumberArray(List<GenericOperandResult> rawOperands)
        {
            List<double> numbers = new List<double>();

            foreach (var operand in rawOperands)
            {
                numbers.Add(operand.AsDouble());
            }

            return numbers;
        }
        protected GenericOperandResult Delta(List<GenericOperandResult> rawOperands)
        {
            List<double> values = new List<double>();

            List<double> numbers = NumberArray(rawOperands).AsList();

            double value = 0;
            foreach (var number in numbers)
            {
                value += number;
                values.Add(value);
            }

            return values;
        }

        protected (byte HiNibble, byte LoNibble) ParseNibbles(byte raw)
        {
            return ( HiNibble: (byte)(raw >> 4), LoNibble: (byte)(raw & 0x0F) );
        }

        protected bool GetTokenFromNibble(byte nibble, out string token)
        {
            token = String.Empty;

            if (nibble >= 0 && nibble <= 9)
            {
                token = nibble.ToString();
            }
            else if (nibble == 0xA)
            {
                token = ".";
            }
            else if (nibble == 0xB)
            {
                token = "E+";
            }
            else if (nibble == 0xC)
            {
                token = "E-";
            }
            else if (nibble == 0xD)
            {
                // not used
            }
            else if (nibble == 0xE)
            {
                token = "-";
            }
            else if (nibble == 0xF)
            {
                return false;
            }

            return true;
        }
        
        private GenericOperandResult GetOperatorValue(DictOperatorsType opType)
        {
            if (!OperatorsRawValues.TryGetValue(opType, out var operands))
            {
                throw new InvalidDataException($"The CFF DICT lacks {opType}, which has no default.");
            }

            return operatorsActions[opType].Invoke(operands);
        }

        public GenericOperandResultSet GetAllAvailableOperands()
        {
            var dict = new Dictionary<DictOperatorsType, GenericOperandResult>();
            
            foreach (var kvp in OperatorsRawValues)
            {
                dict[kvp.Key] = operatorsActions[kvp.Key].Invoke(kvp.Value);
            }

            return new GenericOperandResultSet(dict);
        }
        
        protected virtual void FillDefaultValues()
        {
        }
        
        private void FillOperatorsRawValues(CFFFont font, byte[] rawData)
        {
            var reader = new CffDictReader(rawData);
            var rawOperands = new List<GenericOperandResult>();
            var variationStoreIndex = 0;

            while (!reader.AtEnd)
            {
                ushort token = reader.Peek();
                var operatorLength = 1;
                if (token == 12)
                {
                    token = (ushort)((12 << 8) | reader.Peek(1));
                    operatorLength = 2;
                }

                if (byteToOperatorMap.ContainsKey(token))
                {
                    for (var i = 0; i < operatorLength; i++)
                    {
                        reader.Read();
                    }

                    if ((DictOperatorsType)token == DictOperatorsType.blend)
                    {
                        if (rawOperands.Count == 0 || font.VariationStore?.ItemVariationData == null ||
                            variationStoreIndex < 0 || variationStoreIndex >= font.VariationStore.ItemVariationData.Length)
                        {
                            throw new InvalidDataException("A CFF DICT blends without variation data.");
                        }

                        var blendedOperandsCount = rawOperands.Last().AsInt();
                        var variationData = font.VariationStore.ItemVariationData[variationStoreIndex];
                        var regionCount = variationData.RegionIndexCount;
                        var overallBlendOperandsCount = blendedOperandsCount * (regionCount + 1) + 1;

                        var startIndexOfBlendOperands = rawOperands.Count - overallBlendOperandsCount;
                        if (blendedOperandsCount < 0 || startIndexOfBlendOperands < 0)
                        {
                            throw new InvalidDataException("A CFF DICT blends more operands than it holds.");
                        }

                        var blendOperands = rawOperands.Skip(startIndexOfBlendOperands).ToList();

                        rawOperands = rawOperands.GetRange(0, (int)startIndexOfBlendOperands);

                        var blendedOperands = blendOperands.GetRange(0, (int)blendedOperandsCount);
                        //var deltas = blendOperands.ToArray()[(int)(blendedOperandsCount)..^1];
                        var deltas = blendOperands.Skip((int)blendedOperandsCount).Take(blendOperands.Count - (int)blendedOperandsCount - 1).ToList();

                        for (var op = 0; op < blendedOperands.Count; ++op)
                        {
                            var blendData = new RegionData { RegionIndices = variationData.RegionIndices };

                            for (var region = 0; region < regionCount; ++region)
                            {
                                blendData.Data.Add(deltas[op * regionCount + region].AsDouble());
                            }

                            var genericResult = blendedOperands[op];
                            genericResult.BlendData = blendData;
                            blendedOperands[op] = genericResult;
                        }

                        rawOperands.AddRange(blendedOperands);

                    }
                    else
                    {
                        if ((DictOperatorsType)token == DictOperatorsType.vsindex)
                        {
                            Require(rawOperands, 1);
                            variationStoreIndex = rawOperands.Last().AsInt();
                        }

                        OperatorsRawValues[(DictOperatorsType)token] = rawOperands;
                        rawOperands = new List<GenericOperandResult>();
                    }
                }
                else if (reader.Peek() <= 21)
                {
                    for (var i = 0; i < operatorLength; i++)
                    {
                        reader.Read();
                    }

                    rawOperands = new List<GenericOperandResult>();
                }
                else
                {
                    rawOperands.Add(Number(reader));
                }
            }
        }
        
        public bool IsOperatorAvailable(DictOperatorsType opType)
        {
            return OperatorsRawValues.ContainsKey(opType);
        }

        public GenericOperandResult this[DictOperatorsType op] => GetOperatorValue(op);
    }   
}
