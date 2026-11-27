namespace Adamantium.Pulsar.Mixing;

internal enum CommandKind
{
    Play,
    Stop,
    SetVoicePaused,
    SetVoiceVolume,
    SetVoicePitch,
    SetVoicePan,
    SetVoicePlace,
    SetListener,
    AddBus,
    RemoveBus,
    SetBusVolume,
    SetBusMuted,
    SetBusSolo,
    SetBusPaused,
    KeepBusPlayed,
}
