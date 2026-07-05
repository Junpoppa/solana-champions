using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GameCreator.Runtime.Common;
using UnityEngine;

namespace GameCreator.Runtime.Characters
{
    [Serializable]
    public class Combat
    {
        public const int DEFAULT_LAYER_WEAPON = 5;
        public const int DEFAULT_LAYER_SHIELD = DEFAULT_LAYER_WEAPON + 1;
        
        private static readonly Color GIZMO_BLOCK_ON = new Color(0f, 1f, 0f, 0.5f);
        private static readonly Color GIZMO_BLOCK_OFF = new Color(1f, 1f, 0f, 0.1f);
        
        // MEMBERS: -------------------------------------------------------------------------------

        [NonSerialized] private Target m_Target;
        
        [NonSerialized] private Dictionary<int, Weapon> m_Weapons;
        [NonSerialized] private Dictionary<int, IMunition> m_Munitions;
        [NonSerialized] private Dictionary<int, IStance> m_Stances;

        [NonSerialized] private Character m_Character;
        [NonSerialized] private Args m_Args;
        
        [NonSerialized] private IShield m_Shield;
        
        [NonSerialized] private bool m_IsBlocking;
        [NonSerialized] private float m_BlockStartTime;
        [NonSerialized] private float m_BlockHitTime;
        
        [NonSerialized] private float m_MaxDefense;
        [NonSerialized] private float m_CurDefense;

        // PROPERTIES: ----------------------------------------------------------------------------

        public IShield Shield => this.m_Shield;
        
        public float MaximumDefense
        {
            get => this.m_MaxDefense;
            set => this.m_MaxDefense = Math.Max(0f, value);
        }
        
        public float CurrentDefense
        {
            get => this.m_CurDefense;
            set => this.m_CurDefense = Math.Clamp(value, 0f, this.m_MaxDefense);
        }

        public bool IsBlocking 
        {
            get => this.m_IsBlocking;
            set
            {
                if (this.m_IsBlocking == value && this.m_IsBlocking) return;
                if (this.m_Character.Busy.IsBusy) return;

                this.m_BlockStartTime = this.m_Character.Time.Time;
                if (this.m_IsBlocking == value) return;
                
                this.m_IsBlocking = value;
                switch (this.m_IsBlocking)
                {
                    case true: this.OnStartBlocking(); break;
                    case false: this.OnStopBlocking(); break;
                }
            }
        }

        public float BlockStartTime => this.m_IsBlocking
            ? this.m_BlockStartTime 
            : -1f;
        
        public GameObject Target
        {
            get => this.m_Target.On;
            set => this.m_Target.On = value;
        }
        
        public Weapon[] Weapons
        {
            get
            {
                List<Weapon> weapons = new List<Weapon>();
                foreach (KeyValuePair<int, Weapon> entry in this.m_Weapons)
                {
                    weapons.Add(entry.Value);
                }

                return weapons.ToArray();
            }
        }

        public IMunition[] Munitions
        {
            get
            {
                List<IMunition> munitions = new List<IMunition>();
                foreach (KeyValuePair<int, IMunition> entry in this.m_Munitions)
                {
                    munitions.Add(entry.Value);
                }

                return munitions.ToArray();
            }
        }

        // EVENTS: --------------------------------------------------------------------------------

        public event Action<IWeapon, GameObject> EventEquip;
        public event Action<IWeapon, GameObject> EventUnequip;
        
        // CONSTRUCTOR: ---------------------------------------------------------------------------

        public Combat()
        {
            this.m_Target = new Target();
            this.m_Weapons = new Dictionary<int, Weapon>();
            this.m_Munitions = new Dictionary<int, IMunition>();
            this.m_Stances = new Dictionary<int, IStance>();
        }
        
        // INITIALIZE METHODS: --------------------------------------------------------------------
        
        internal void OnStartup(Character character)
        {
            this.m_Character = character;
            this.m_Args = new Args(character, character);
        }
        
        internal void AfterStartup(Character character)
        { }

        internal void OnDispose(Character character)
        {
            this.m_Character = character;
            this.m_Args = new Args(character, character);
        }

        internal void OnEnable()
        {
            foreach (KeyValuePair<int, IStance> entry in this.m_Stances)
            {
                entry.Value.OnEnable(this.m_Character);
            }
        }

        internal void OnDisable()
        {
            foreach (KeyValuePair<int, IStance> entry in this.m_Stances)
            {
                entry.Value.OnDisable(this.m_Character);
            }
        }
        
        // UPDATE METHODS: ------------------------------------------------------------------------

        internal void OnLateUpdate()
        {
            this.CalculateDefense();

            foreach (KeyValuePair<int, IStance> entry in this.m_Stances)
            {
                entry.Value.OnUpdate();
            }
        }

        // GETTERS: -------------------------------------------------------------------------------

        public TMunitionValue RequestMunition(IWeapon weapon)
        {
            if (weapon == null) return null;
            if (this.m_Munitions.TryGetValue(weapon.Id.Hash, out IMunition munition))
            {
                return munition.Value;
            }

            munition = new Munition(weapon.Id.Hash, weapon.CreateMunition()); 
            this.m_Munitions.Add(weapon.Id.Hash, munition);

            return munition.Value;
        }

        public T RequestStance<T>() where T : IStance, new()
        {
            int stanceId = typeof(T).GetHashCode();
            if (this.m_Stances.TryGetValue(stanceId, out IStance stance))
            {
                return (T) stance;
            }

            T newStance = new T();
            newStance.OnEnable(this.m_Character);

            this.m_Stances.Add(stanceId, newStance);
            return newStance;
        }
        
        public ReactionOutput GetReaction(ReactionInput input, Args args, IReaction reaction)
        {
            if (reaction?.CanRun(this.m_Character, args, input) ?? false)
            {
                return reaction.Run(this.m_Character, args, input);
            }

            foreach (Weapon weapon in this.m_Character.Combat.Weapons)
            {
                if (weapon.Asset.HitReaction == null) continue;
                if (!weapon.Asset.HitReaction.CanRun(this.m_Character, args, input)) continue;
                
                return weapon.Asset.HitReaction.Run(this.m_Character, args, input);
            }

            Reaction defaultReaction = this.m_Character.Animim.Reaction;
            if (defaultReaction == null) return ReactionOutput.None;

            return defaultReaction.CanRun(this.m_Character, args, input) 
                ? defaultReaction.Run(this.m_Character, args, input)
                : ReactionOutput.None;
        }

        // PUBLIC METHODS: ------------------------------------------------------------------------

        public bool IsEquipped(IWeapon weapon)
        {
            return weapon != null && this.m_Weapons.ContainsKey(weapon.Id.Hash);
        }

        public async Task Equip(IWeapon asset, GameObject instance, Args args)
        {
            if (asset == null) return;
            if (this.IsEquipped(asset)) return;
            
            Weapon weapon = new Weapon(asset, instance);
            this.m_Weapons.Add(asset.Id.Hash, weapon);

            if (asset.Shield != null)
            {
                this.m_Shield = this.FindShield();
            }

            if (!this.m_Munitions.ContainsKey(asset.Id.Hash))
            {
                Munition munition = new Munition(asset.Id.Hash, asset.CreateMunition());
                this.m_Munitions.Add(asset.Id.Hash, munition);
            }

            await asset.RunOnEquip(args);
            this.EventEquip?.Invoke(asset, instance);
        }

        public async Task Unequip(IWeapon asset, Args args)
        {
            if (asset == null) return;
            if (!this.IsEquipped(asset)) return;

            Weapon weapon = this.m_Weapons[asset.Id.Hash];
            this.m_Weapons.Remove(asset.Id.Hash);
            
            if (asset.Shield != null)
            {
                this.m_Shield = this.FindShield();
            }

            await asset.RunOnUnequip(args);
            this.EventUnequip?.Invoke(asset, weapon.Instance);
        }

        public GameObject GetProp(IWeapon asset)
        {
            if (asset == null) return null;
            return this.m_Weapons.TryGetValue(asset.Id.Hash, out Weapon weapon)
                ? weapon.Instance
                : null;
        }

        public IShield Block(ShieldInput input, Args args, out ShieldOutput output)
        {
            if (this.m_Shield == null)
            {
                output = ShieldOutput.NO_BLOCK;
                return null;
            }

            this.m_BlockHitTime = this.m_Character.Time.Time;
            ShieldOutput weaponOutput = this.m_Shield.CanDefend(this.m_Character, args, input);

            output = weaponOutput;
            return weaponOutput.Type != BlockType.None ? this.m_Shield : null;
        }
        
        // PRIVATE METHODS: -----------------------------------------------------------------------

        private IShield FindShield()
        {
            int highestPriority = -1;
            IShield highestShield = null;

            foreach (KeyValuePair<int, Weapon> weaponsEntry in this.m_Weapons)
            {
                IShield shield = weaponsEntry.Value.Asset.Shield;
                if ((shield?.Priority ?? -1) <= highestPriority) continue;

                highestPriority = shield?.Priority ?? -1;
                highestShield = shield;
            }

            return highestShield;
        }

        private void CalculateDefense()
        {
            if (this.m_Shield != null)
            {
                float maxDefense = this.m_Shield.GetDefense(this.m_Args);
                float recoveryRate = this.m_Shield.GetRecovery(this.m_Args);

                float recoverDefense = this.m_BlockHitTime + this.m_Shield.GetCooldown(this.m_Args);
                float defense = this.m_Character.Time.Time >= recoverDefense
                    ? this.CurrentDefense + recoveryRate * this.m_Character.Time.DeltaTime
                    : this.CurrentDefense;
                
                this.MaximumDefense = maxDefense;
                this.CurrentDefense = Math.Clamp(defense, 0f, maxDefense);   
            }
            else
            {
                this.MaximumDefense = 0f;
                this.CurrentDefense = 0f;
            }
        }
        
        private void OnStartBlocking()
        {
            Weapon[] weapons = this.Weapons;
            foreach (Weapon weapon in weapons)
            {
                weapon.Asset.Shield?.OnRaise(this.m_Character);
            }
        }

        private void OnStopBlocking()
        {
            Weapon[] weapons = this.Weapons;
            foreach (Weapon weapon in weapons)
            {
                weapon.Asset.Shield?.OnLower(this.m_Character);
            }
        }
        
        // GIZMOS: --------------------------------------------------------------------------------

        internal void OnDrawGizmos(Character character)
        {
            if (!Application.isPlaying) return;
            if (this.m_Shield == null) return;

            float angle = this.m_Shield.GetAngle(new Args(character));
            Gizmos.color = this.IsBlocking ? GIZMO_BLOCK_ON : GIZMO_BLOCK_OFF;
            
            GizmosExtension.Arc(
                character.Feet + Vector3.up * 0.05f,
                character.transform.rotation,
                angle,
                character.Motion.Radius + 0.2f,
                character.Motion.Radius + 0.5f
            );
        }
    }
}