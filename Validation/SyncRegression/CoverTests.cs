using System;
using System.Collections.Generic;
using EFT;
using UnityEngine;
using UnityEngine.AI;
using ISBSpecialForces.Behavior;
using ISBSpecialForces.Behavior.ISB;

internal static class CoverTests
{
    internal static void Run()
    {
        Time.frameCount = 100;
        Time.time = 10;
        var bot = new BotOwner();
        bot.Memory.GoalEnemy = new CoverEnemy { EnemyLastPosition = new Vector3(10, 0, 0) };
        var safe = new CustomNavigationPoint { Position = new Vector3(-4, 0, 0) };
        var data = new CoverSearchData { CenterPos = new Vector3(1, 0, 0), MaxDistSqr = 400 };
        var selected = ISBRetreatCover.Find(bot, data, (query, current) => safe, false);
        Tests.Check(selected == safe, "safe retreat selected");
        Tests.Check(data.MaxDistSqr == 400 && data.CenterPos.x == 1, "retreat query restored");
        ISBNavigationBudget.TryBeginSearch(Time.frameCount);
        int calls = NavMesh.Calls;
        Tests.Check(ISBRetreatCover.Find(bot, data, (query, current) => throw new Exception("unexpected search"), false) == safe, "budget fallback uses validated cover");
        Tests.Check(NavMesh.Calls == calls, "exhausted budget cannot calculate path");
        Time.time += 2;
        Tests.Check(ISBRetreatCover.Find(bot, data, (query, current) => safe, false) == null, "expired cover is not reused");
        Time.frameCount++;
        bot.Memory.CurCustomCoverPoint = new CustomNavigationPoint { Position = new Vector3(9, 0, 0) };
        ISBRetreatCover.DiscardUnsafe(bot);
        Tests.Check(bot.Memory.CurCustomCoverPoint == null && bot.Stopped, "unsafe retreat cleared with available budget");
        Tests.Check(!ISBRetreatCover.SafePath(Vector3.zero, new Vector3(10, 0, 0), new[] { Vector3.zero, new Vector3(9, 0, 0) }), "path toward threat rejected");
    }
}

namespace EFT
{
    public class CustomNavigationPoint { public Vector3 Position; }
    public class CoverEnemy { public Vector3 EnemyLastPosition; }
    public class CoverDamage { public Vector3 Postion; }
    public class CoverMemory
    {
        public CoverEnemy GoalEnemy;
        public bool LastDamageDataActive, IsInCover;
        public float LastTimeHit;
        public Vector3 LastHitPos;
        public CoverDamage LastDamageData = new CoverDamage();
        public CustomNavigationPoint CurCustomCoverPoint;
        public void SetCoverPoints(CustomNavigationPoint point) { CurCustomCoverPoint = point; }
    }
    public class BotOwner
    {
        public Vector3 Position;
        public CoverMemory Memory = new CoverMemory();
        public bool Stopped;
        public void StopMove() { Stopped = true; }
    }
    public class CoverSearchData
    {
        public Vector3 CenterPos;
        public float MaxDistSqr;
        public List<Vector3> CarePositions;
    }
}

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
        public static Vector3 zero => new Vector3();
        public static Vector3 up => new Vector3(0,1,0);
        public float sqrMagnitude => x*x+y*y+z*z;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public void Normalize() { float m=magnitude; if(m>0) {x/=m;y/=m;z/=m;} }
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
        public static Vector3 operator /(Vector3 a,float b)=>new Vector3(a.x/b,a.y/b,a.z/b);
        public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
        public static Vector3 Cross(Vector3 a,Vector3 b)=>new Vector3(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
    }
    public static class Mathf
    {
        public static float Min(float a,float b)=>Math.Min(a,b);
        public static float Max(float a,float b)=>Math.Max(a,b);
        public static float Clamp01(float a)=>Math.Max(0,Math.Min(1,a));
    }
}

namespace UnityEngine.AI
{
    public enum NavMeshPathStatus { PathComplete, PathInvalid }
    public class NavMeshPath { public NavMeshPathStatus status; public Vector3[] corners; }
    public static class NavMesh
    {
        public const int AllAreas=-1;
        public static int Calls;
        public static bool CalculatePath(Vector3 from,Vector3 to,int areas,NavMeshPath path)
        {
            Calls++;
            path.status=NavMeshPathStatus.PathComplete;
            path.corners=new[] {from,to};
            return true;
        }
    }
}
