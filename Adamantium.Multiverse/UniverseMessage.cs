using System;

namespace Adamantium.Multiverse;

internal readonly record struct UniverseMessage(UniverseOutput Output, Action Action);
