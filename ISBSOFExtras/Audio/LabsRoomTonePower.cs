using System;
using System.Reflection;
using Audio.AmbientSubsystem;
using Audio.SpatialSystem;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using HarmonyLib;

namespace ISBSSOFExtra
{
    internal static class LabsRoomTonePower
    {
        private static Harmony _harmony;
        private static GameWorld _mutedWorld;
        private static FieldInfo _toneControllerField;
        private static FieldInfo _roomStorageField;
        private static FieldInfo _roomPlayerField;

        internal static void Enable(ManualLogSource log)
        {
            if (_harmony != null)
                return;
            try
            {
                _toneControllerField = AccessTools.Field(typeof(AmbientAudioSystem), "_roomToneController");
                _roomStorageField = AccessTools.Field(typeof(SpatialAudioSystem), "_audioRoomStorage");
                _roomPlayerField = AccessTools.Field(typeof(SpatialAudioRoom), "_roomAmbientSoundPlayer");
                var changeTone = AccessTools.Method(typeof(RoomToneController), nameof(RoomToneController.ChangeRoomTone), new[] { typeof(ISpatialAudioRoom) });
                if (changeTone == null || _toneControllerField == null || _roomStorageField == null || _roomPlayerField == null)
                    throw new MissingMemberException("Laboratory room-tone audio members");
                var harmony = new Harmony("com.samc137.isbsofextras.labs-room-tone");
                harmony.Patch(changeTone, prefix: new HarmonyMethod(typeof(LabsRoomTonePower), nameof(BeforeRoomToneChange)));
                _harmony = harmony;
            }
            catch (Exception ex)
            {
                log?.LogError("ISB SOF: Labs room-tone suppression unavailable: " + ex.GetBaseException().Message);
            }
        }

        private static bool BeforeRoomToneChange() =>
            _mutedWorld == null || !ReferenceEquals(_mutedWorld, Singleton<GameWorld>.Instance);

        internal static void Mute()
        {
            var world = Singleton<GameWorld>.Instance;
            if (_harmony == null || world == null || ReferenceEquals(_mutedWorld, world))
                return;

            _mutedWorld = world;
            var spatial = MonoBehaviourSingleton<SpatialAudioSystem>.Instance;
            var storage = spatial == null ? null : _roomStorageField.GetValue(spatial) as AudioRoomStorage;
            var controller = GetController();
            StopRoom(controller?._prevEnteredRoom);
            if (storage?._validSpatialAudioRooms != null)
                foreach (var room in storage._validSpatialAudioRooms)
                    StopRoom(room);
            controller?.Clear();
        }

        private static void StopRoom(ISpatialAudioRoom room)
        {
            if (!(room is SpatialAudioRoom spatialRoom) || spatialRoom == null ||
                !(_roomPlayerField.GetValue(spatialRoom) is ISoundPlayer player) || !player.IsPlaying)
                return;
            if (player is RoomAmbientSoundPlayer tone)
                tone.TryReleaseSourceIfNotNull();
            else
                player.Stop(true);
        }

        internal static void Restore()
        {
            var world = _mutedWorld;
            _mutedWorld = null;
            if (world == null || !ReferenceEquals(world, Singleton<GameWorld>.Instance))
                return;

            var controller = GetController();
            var room = MonoBehaviourSingleton<SpatialAudioSystem>.Instance?.ListenerCurrentRoom;
            controller?.Clear();
            if (room?.RoomAmbientData != null)
                controller?.ChangeRoomTone(room);
        }

        private static RoomToneController GetController()
        {
            var ambient = MonoBehaviourSingleton<AmbientAudioSystem>.Instance;
            return ambient == null ? null : _toneControllerField.GetValue(ambient) as RoomToneController;
        }

        internal static void Reset() => _mutedWorld = null;

        internal static void Disable()
        {
            Restore();
            _harmony?.UnpatchSelf();
            _harmony = null;
        }
    }
}

