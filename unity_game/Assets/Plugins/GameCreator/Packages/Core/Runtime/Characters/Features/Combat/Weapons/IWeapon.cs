using System.Threading.Tasks;
using GameCreator.Runtime.Common;
using UnityEngine;

namespace GameCreator.Runtime.Characters
{
    public interface IWeapon
    {
        // PROPERTIES: ----------------------------------------------------------------------------
        
        IdString Id { get; }
        Texture EditorIcon { get; }
        
        IReaction HitReaction { get; }
        IShield Shield { get; }

        // GETTERS: -------------------------------------------------------------------------------
        
        string GetName(Args args);
        string GetDescription(Args args);
        
        Sprite GetSprite(Args args);
        Color GetColor(Args args);

        // RUNNERS: -------------------------------------------------------------------------------

        Task RunOnEquip(Args args);
        Task RunOnUnequip(Args args);
        
        // PUBLIC METHODS: ------------------------------------------------------------------------

        TMunitionValue CreateMunition();
    }
}