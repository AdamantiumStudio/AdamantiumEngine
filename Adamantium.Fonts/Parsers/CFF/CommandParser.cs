using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Fonts.Common;
using Adamantium.Fonts.Extensions;
using Adamantium.Fonts.Tables.CFF;

namespace Adamantium.Fonts.Parsers.CFF
{
    internal class CommandParser : OperandParser
    {
        private const int MaxSteps = 500_000;
        private const int MaxPendingBytes = 1 << 20;
        private const int MaxOperands = 513;

        private static Dictionary<ushort, OperatorsType> bytesToOperatorMap;
        private ICFFParser cffParser;
        private readonly FontWorkBudget budget;

        internal CommandParser(ICFFParser cffParser, FontWorkBudget budget)
        {
            this.cffParser = cffParser;
            this.budget = budget;
        }

        static CommandParser()
        {
            bytesToOperatorMap = new Dictionary<ushort, OperatorsType>
            {
                { (ushort)OperatorsType.hstem               , OperatorsType.hstem        },
                { (ushort)OperatorsType.vstem               , OperatorsType.vstem        },
                { (ushort)OperatorsType.vmoveto             , OperatorsType.vmoveto      },
                { (ushort)OperatorsType.rlineto             , OperatorsType.rlineto      },
                { (ushort)OperatorsType.hlineto             , OperatorsType.hlineto      },
                { (ushort)OperatorsType.vlineto             , OperatorsType.vlineto      },
                { (ushort)OperatorsType.rrcurveto           , OperatorsType.rrcurveto    },
                { (ushort)OperatorsType.callsubr            , OperatorsType.callsubr     },
                { (ushort)OperatorsType.@return             , OperatorsType.@return      },
                { (ushort)OperatorsType.and                 , OperatorsType.and          },
                { (ushort)OperatorsType.or                  , OperatorsType.or           },
                { (ushort)OperatorsType.not                 , OperatorsType.not          },
                { (ushort)OperatorsType.abs                 , OperatorsType.abs          },
                { (ushort)OperatorsType.add                 , OperatorsType.add          },
                { (ushort)OperatorsType.sub                 , OperatorsType.sub          },
                { (ushort)OperatorsType.div                 , OperatorsType.div          },
                { (ushort)OperatorsType.neg                 , OperatorsType.neg          },
                { (ushort)OperatorsType.eq                  , OperatorsType.eq           },
                { (ushort)OperatorsType.drop                , OperatorsType.drop         },
                { (ushort)OperatorsType.put                 , OperatorsType.put          },
                { (ushort)OperatorsType.get                 , OperatorsType.get          },
                { (ushort)OperatorsType.ifelse              , OperatorsType.ifelse       },
                { (ushort)OperatorsType.random              , OperatorsType.random       },
                { (ushort)OperatorsType.mul                 , OperatorsType.mul          },
                { (ushort)OperatorsType.sqrt                , OperatorsType.sqrt         },
                { (ushort)OperatorsType.dup                 , OperatorsType.dup          },
                { (ushort)OperatorsType.exch                , OperatorsType.exch         },
                { (ushort)OperatorsType.index               , OperatorsType.index        },
                { (ushort)OperatorsType.roll                , OperatorsType.roll         },
                { (ushort)OperatorsType.hflex               , OperatorsType.hflex        },
                { (ushort)OperatorsType.flex                , OperatorsType.flex         },
                { (ushort)OperatorsType.hflex1              , OperatorsType.hflex1       },
                { (ushort)OperatorsType.flex1               , OperatorsType.flex1        },
                { (ushort)OperatorsType.endchar             , OperatorsType.endchar      },
                { (ushort)OperatorsType.hstemhm             , OperatorsType.hstemhm      },
                { (ushort)OperatorsType.hintmask            , OperatorsType.hintmask     },
                { (ushort)OperatorsType.cntrmask            , OperatorsType.cntrmask     },
                { (ushort)OperatorsType.rmoveto             , OperatorsType.rmoveto      },
                { (ushort)OperatorsType.hmoveto             , OperatorsType.hmoveto      },
                { (ushort)OperatorsType.vstemhm             , OperatorsType.vstemhm      },
                { (ushort)OperatorsType.rcurveline          , OperatorsType.rcurveline   },
                { (ushort)OperatorsType.rlinecurve          , OperatorsType.rlinecurve   },
                { (ushort)OperatorsType.vvcurveto           , OperatorsType.vvcurveto    },
                { (ushort)OperatorsType.hhcurveto           , OperatorsType.hhcurveto    },
                { (ushort)OperatorsType.callgsubr           , OperatorsType.callgsubr    },
                { (ushort)OperatorsType.vhcurveto           , OperatorsType.vhcurveto    },
                { (ushort)OperatorsType.hvcurveto           , OperatorsType.hvcurveto    },
                { (ushort)OperatorsType.vsindex             , OperatorsType.vsindex      },
                { (ushort)OperatorsType.blend               , OperatorsType.blend        },
            };
        }

        public List<Command> Parse(CFFFont font, Stack<byte> mainStack, FontDict fontDict, int index = 0)
        {
            var commands = new List<Command>();
            
            var operands = new List<CommandOperand>();
            ushort token;
            bool clearOperands;
            bool isBlendPresent = false;
            int stemCount = 0;

            var fontDictBias = 0;
            if (fontDict?.LocalSubr != null)
            {
                fontDictBias = cffParser.CalculateSubrBias((uint)fontDict.LocalSubr.Count);
            }

            var variationStoreIndex = fontDict?.VariationStoreIndex ?? 0;

            var steps = 0;
            while (mainStack.Count > 0)
            {
                if (++steps > MaxSteps || mainStack.Count > MaxPendingBytes || operands.Count > MaxOperands)
                {
                    throw new InvalidDataException("A charstring runs too long or calls itself.");
                }

                budget.Spend(1);

                token = mainStack.Pop();

                if (token == 12) // escape byte found, it is a 2-byte operator
                {
                    token = (ushort)((12 << 8) | mainStack.Pop());
                }

                if (bytesToOperatorMap.ContainsKey(token)) // this is operator
                {
                    clearOperands = true;

                    // TO DO: add mechanism to process operators like 'add' - not resulting in new Command, but instead pushes its result to the top of the stack
                    switch ((OperatorsType)token)
                    {
                        case OperatorsType.and:
                            mainStack.Push(Convert.ToByte(Convert.ToBoolean(operands[0].Value) && Convert.ToBoolean(operands[1].Value)));
                            break;
                        case OperatorsType.or:
                            mainStack.Push(Convert.ToByte(Convert.ToBoolean(operands[0].Value) || Convert.ToBoolean(operands[1].Value)));
                            break;
                        case OperatorsType.not:
                            mainStack.Push(Convert.ToByte(!Convert.ToBoolean(operands[0].Value)));
                            break;
                        case OperatorsType.eq:
                            mainStack.Push(Convert.ToByte(Convert.ToBoolean(operands[0].Value) == Convert.ToBoolean(operands[1].Value)));
                            break;
                        case OperatorsType.drop:
                            operands.RemoveAt(0);
                            break;
                        case OperatorsType.ifelse:
                            clearOperands = false;

                            var res = (operands[2] <= operands[3]) ? operands[0] : operands[1];
                            operands.Clear();
                            operands.Add(res);
                            break;
                        case OperatorsType.random:
                            clearOperands = false;

                            var random = new Random();

                            var rand = new CommandOperand(-(random.NextDouble() - 1));
                            operands.Clear();
                            operands.Add(rand);
                            break;
                        case OperatorsType.abs:
                            clearOperands = false;

                            operands[0].Value = Math.Abs(operands[0].Value);
                            break;
                        case OperatorsType.add:
                            clearOperands = false;

                            var sum = operands[0] + operands[1];
                            operands.Clear();
                            operands.Add(sum);
                            break;
                        case OperatorsType.sub:
                            clearOperands = false;

                            var diff = operands[0] - operands[1];
                            operands.Clear();
                            operands.Add(diff);
                            break;
                        case OperatorsType.div:
                            clearOperands = false;

                            var quo = operands[0] / operands[1];
                            operands.Clear();
                            operands.Add(quo);
                            break;
                        case OperatorsType.mul:
                            clearOperands = false;

                            var prod = operands[0] * operands[1];
                            operands.Clear();
                            operands.Add(prod);
                            break;
                        case OperatorsType.sqrt:
                            clearOperands = false;

                            operands[0].Value = Math.Sqrt(operands[0].Value);
                            break;
                        case OperatorsType.dup:
                            clearOperands = false;

                            operands.Add(operands[0]);
                            break;
                        case OperatorsType.exch:
                            clearOperands = false;

                            var val = operands[0];
                            operands[0] = operands[1];
                            operands[1] = val;
                            break;
                        case OperatorsType.neg:
                            clearOperands = false;

                            operands[0] = -operands[0];
                            break;
                        case OperatorsType.blend:
                            clearOperands = false;

                            isBlendPresent = true;

                            var blendedOperandsCount =  operands.Last().Value;
                            var variationData = font.VariationStore.ItemVariationData[variationStoreIndex];
                            var regionCount = variationData.RegionIndexCount;
                            var overallBlendOperandsCount = blendedOperandsCount * (regionCount + 1) + 1;

                            var startIndexOfBlendOperands = operands.Count - overallBlendOperandsCount;

                            var blendOperands = operands.Skip((int)startIndexOfBlendOperands).ToList();

                            operands = operands.GetRange(0, (int)startIndexOfBlendOperands);

                            var blendedOperands = blendOperands.GetRange(0, (int) blendedOperandsCount);
                            var deltas = blendOperands.Skip((int)blendedOperandsCount).Take(blendOperands.Count - (int)blendedOperandsCount - 1).ToList();

                            for (var op = 0; op < blendedOperands.Count; ++op)
                            {
                                var blendData = new RegionData { RegionIndices = variationData.RegionIndices };

                                for (var region = 0; region < regionCount; ++region)
                                {
                                    blendData.Data.Add(deltas[op * regionCount + region].Value);
                                }

                                blendedOperands[op].BlendData = blendData;
                            }

                            operands.AddRange(blendedOperands);

                            break;
                        case OperatorsType.hintmask:
                        case OperatorsType.cntrmask:
                            stemCount += operands.Count / 2;
                            
                            if (stemCount == 0) stemCount = 1;
                            var bytesToRead = Math.Ceiling((double) stemCount / 8);
                            for (int i = 0; i < bytesToRead; ++i)
                            {
                                var maskByte = mainStack.Pop();
                            }
                            break;
                        case OperatorsType.callgsubr:
                            if (operands.Count == 0)
                            {
                                throw new ArgumentException($"callgsubr operand count == 0!");
                            }

                            var subrIndex = (int)operands.Last().Value + cffParser.GlobalSubrBias;
                            var subrData = cffParser.GlobalSubroutineIndex.DataByOffset[subrIndex];
                            cffParser.UnpackSubrToStack(subrData, mainStack); 
                            operands.RemoveAt(operands.Count - 1);

                            clearOperands = false;
                            break;
                        case OperatorsType.callsubr:
                            if (operands.Count == 0)
                            {
                                throw new ArgumentException($"callsubr operand count == 0!");
                            }

                            if (font.IsLocalSubroutineAvailable)
                            {
                                var localSubrIndex = (int)operands.Last().Value + font.LocalSubrBias;
                                var localSubrData = font.LocalSubroutineIndex.DataByOffset[localSubrIndex];
                                cffParser.UnpackSubrToStack(localSubrData, mainStack);
                            }
                            else if (fontDict != null) // get local subr from private dict
                            {
                                var localSubrIndex = (int)operands.Last().Value + fontDictBias;
                                var localSubrData = fontDict.LocalSubr[localSubrIndex];
                                cffParser.UnpackSubrToStack(localSubrData, mainStack);
                            }

                            operands.RemoveAt(operands.Count - 1);

                            clearOperands = false;
                            break;
                        case OperatorsType.@return:
                            clearOperands = false;
                            break;
                        case OperatorsType.vsindex:
                            variationStoreIndex = (int)operands.Last().Value;
                            break;
                        default:
                            switch ((OperatorsType)token)
                            {
                                case OperatorsType.vstem:
                                case OperatorsType.hstem:
                                case OperatorsType.hstemhm:
                                case OperatorsType.vstemhm:
                                    stemCount+=operands.Count/2;
                                    break;
                            }

                            var command = new Command();

                            command.Operator = bytesToOperatorMap[token];
                            command.Operands = operands;
                            command.IsBlendPresent = isBlendPresent;
                            commands.Add(command);

                            isBlendPresent = false;
                            if (command.Operator == OperatorsType.endchar)
                            {
                                return commands;
                            }

                            break;
                    }

                    if (clearOperands)
                    {
                        operands = new List<CommandOperand>();
                    }
                }
                else // this is operand
                {
                    operands.Add(new (Number((byte)token, mainStack).AsDouble()));
                }
            }

            return commands;
        }
    }
}
