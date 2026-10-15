using System;
using System.Collections.Generic;
using EFT;
using EFT.CameraControl;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using RootMotion.FinalIK;
using UnityEngine;
#if ISB_SPT41
using BitPacking;
using Diz.LanguageExtensions;
using EFT.Ballistics;
using EFT.Character;
using IdentityOwner = EFT.IObserverToPlayerBridge;
using IdentityDamage = EFT.Ballistics.DamageInfo;
using IdentityHit = EFT.PlayerHitInfo;
using IdentityShot = EFT.Ballistics.ShotId;
using IdentityMovement = EFT.Character.IMovementContextAdapter;
using IdentityDescriptor = EFT.ProfileDescriptor;
using IdentitySettings = EFT.ProfileSettings;
using IdentityInfo = EFT.ProfileInfoDescriptor;
#else
using IdentityOwner = IPlayerOwner;
using IdentityDamage = DamageInfoStruct;
using IdentityHit = ShotInfoClass;
using IdentityShot = ShotIdStruct;
using IdentityMovement = GInterface270;
using IdentityDescriptor = CompleteProfileDescriptorClass;
using IdentitySettings = ProfileInfoSettingsClass;
using IdentityInfo = ProfileInfoClass;

#endif
namespace ISBSSOFExtra
{
    public sealed class Scp173Identity : IPlayer, IdentityOwner
    {
        private readonly Scp173Event _event;
        private readonly BifacialTransform _transform;
        private bool _disposed;
        internal Scp173Identity(Scp173Event controller, Transform transform, string entity, Profile template)
        {
            _event = controller;
            EntityId = entity;
            _transform = new BifacialTransform
            {
                Original = transform
            };
            var descriptor = new IdentityDescriptor
            {
                Info = new IdentityInfo(template.Info)
            };
#if ISB_SPT41
            descriptor.Inventory = new InventoryDescriptor
            {
                _equipmentId = entity.Substring(8, 24),
                _items = new[] { new JsonType.FlatItem { _id = entity.Substring(8, 24), _tpl = template.Inventory.Equipment.TemplateId } }
            };
#else
            descriptor.Inventory = new EFTInventoryClass
            {
                MongoID_0 = entity.Substring(8, 24),
                Gclass1390_0 = new[]
                {
                    new FlatItemsDataClass
                    {
                        _id = entity.Substring(8, 24),
                        _tpl = template.Inventory.Equipment.TemplateId
                    }
                }
            };
#endif
            descriptor.Id = entity.Substring(0, 24);
            descriptor.AccountId = "0";
            descriptor.Info.Nickname = "SCP-173";
            descriptor.Info.MainProfileNickname = "";
            descriptor.Info.Side = EPlayerSide.Savage;
            descriptor.Info.GroupId = "ISB_SCP173";
            descriptor.Info.TeamId = "ISB_SCP173";
            descriptor.Info.MemberCategory = EMemberCategory.Default;
            descriptor.Info.SelectedMemberCategory = EMemberCategory.Default;
            descriptor.Info.Settings = new IdentitySettings
            {
                Role = WildSpawnType.assault
            };
            descriptor.Info.PrestigeLevel = 0;
            descriptor.Info.Level = 1;
            descriptor.Info.Experience = 0;
            Profile = new Profile(descriptor);
        }

        public string EntityId { get; }
        public string EntityType => "SCP173";
        public string Nickname => "SCP-173";
        public bool Active => _event.DebugActive;
        public bool Observed => _event.DebugObserved;

        public bool Kill() => _event.RequestDebug("kill", Position);
        public bool Teleport(Vector3 position) => _event.RequestDebug("teleport", position);
        public bool TeleportTo(Player player, float distance = 5f) => _event.DebugBringTo(player, distance);
        public bool TeleportPlayerTo(Player player) => _event.DebugGoTo(player);
        public int Id => -173;
        public EPlayerSide Side => EPlayerSide.Savage;
        public string GroupId => "ISB_SCP173";
        public string TeamId => "ISB_SCP173";
        public Vector3 LookDirection => _transform.Original != null ? _transform.Original.forward : Vector3.forward;
        public Vector3 Position => _event.DebugPosition;
        public BifacialTransform Transform => _transform;
        public BifacialTransform WeaponRoot => _transform;
        public IHealthController HealthController => null;
        public Profile Profile { get; }
        public IAIData AIData => null;
        public PlayerLoyaltyData Loyalty => null;
        public bool IsAI => true;
        public Dictionary<BodyPartType, EnemyPart> MainParts { get; } = new Dictionary<BodyPartType, EnemyPart>();
        public string ProfileId => Profile.Id;
        public string AccountId => "0";
        public InventoryController InventoryController => null;
        public IPlayerSearchController SearchController => null;
        public PlayerBones PlayerBones => null;
        public bool IsYourPlayer => false;
        public PlayerBody PlayerBody => null;
        public ICharacterController CharacterController => null;
        public bool IsInBufferZone { get; set; }
        public EPlayerBtrState BtrState { get; set; }
        public bool StateIsSuitableForHandInput => false;
        public Vector2 Rotation => new Vector2(_transform.Original != null ? _transform.Original.eulerAngles.y : 0f, 0f);
        public Vector3 Velocity => Vector3.zero;
        public Player.EUpdateMode ArmsUpdateMode => Player.EUpdateMode.Manual;
        public EUpdateQueue ArmsUpdateQueue => EUpdateQueue.Update;
        public ECameraType VisibleToCameraType => ECameraType.Default;
        public bool IsVisibleToCamera => Observed;

        public event Action<IPlayer> OnIPlayerDeadOrUnspawn;
        public IPlayer iPlayer => this;
        public float AverageRotationSpeed => 0f;
        public float BotSoundCoef => 1f;
        public float SoundRadius => 0f;
        public EPlayerState CurrentStataName => EPlayerState.Idle;
        public float CharacterSpeed => 0f;
        public bool IsZombie => false;
        public WeaponSoundPlayer WeaponSoundPlayer => null;
        public HitReaction HitReaction => null;
        public IdentityMovement MovementContextAdapter => null;
        public EPointOfView PointOfView => EPointOfView.ThirdPerson;
        public string ItemInHandTemplateID => null;

        public bool HasBodyPartCollider(Collider collider) => false;
        public void ShotReactions(IdentityDamage damage, EBodyPart bodyPart)
        {
        }

        public IdentityHit ApplyShot(IdentityDamage damage, EBodyPart bodyPart, EBodyPartColliderType colliderType, EArmorPlateCollider armorPlateCollider, IdentityShot shotId) => default;
#if ISB_SPT41
        public Option<Item> FindItemById(MongoID itemId, bool checkDistance = true, bool checkOwnership = true) => default;
        public void OnDeserializeFromServer(byte channelId, IBitReaderStream reader) { }
        public CultistAmulet FindCultistAmulet() => null;
        public bool HasMarkOfUnknown(out MarkOfUnknown mark) { mark = null; return false; }
#else
        public GStruct156<Item> FindItemById(MongoID itemId, bool checkDistance = true, bool checkOwnership = true) => default;
        public void OnDeserializeFromServer(byte channelId, IDataReader reader)
        {
        }

        public CultistAmuletItemClass FindCultistAmulet() => null;
        public bool HasMarkOfUnknown(out MarkOfUnknownItemClass mark)
        {
            mark = null;
            return false;
        }

#endif
        public RadioTransmitterRecodableComponent FindRadioTransmitter() => null;
        public void SetInteractInHands(EInteraction interaction)
        {
        }

        public void PlantItemLocalOnly(Item item, string zone)
        {
        }

        public void UpdateInteractionCast()
        {
        }

        public void HandleFlareSuccessEvent(Vector3 position, AmmoTemplate ammo)
        {
        }

        public Vector3 PlayerColliderPointOnCenterAxis(float height) => Position + Vector3.up * Mathf.Clamp01(height) * 2f;
        public IAnimator GetArmsAnimatorCommon() => null;
        public void SetArmsAnimatorCommon(IAnimator animator)
        {
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            OnIPlayerDeadOrUnspawn?.Invoke(this);
        }
    }

    public static class Scp173Registry
    {
        public static Scp173Identity Current { get; internal set; }

        public static Scp173Identity Find(string id)
        {
            var entity = Current;
            return entity != null && (entity.ProfileId == id || entity.EntityId == id) ? entity : null;
        }
    }
}
