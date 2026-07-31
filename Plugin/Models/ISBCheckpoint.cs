using EFT;
using System.Collections.Generic;
using UnityEngine;

namespace ISBSpecialForces.Models
{
    public class ISBCheckpoint
    {
        public BotZone Zone { get; set; }
        public Vector3 Position { get; set; }
        public float Radius { get; set; } = 20f;
        public List<BotOwner> AssignedBots { get; set; } = new List<BotOwner>();

        public ISBCheckpoint(BotZone zone, Vector3 position, float radius = 20f)
        {
            Zone = zone;
            Position = position;
            Radius = radius;
        }
    }
}
