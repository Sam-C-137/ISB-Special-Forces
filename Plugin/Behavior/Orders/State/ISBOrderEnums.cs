namespace ISBSpecialForces.Behavior.Orders.State
{
    public enum ISBOrderType
    {
        None = 0,
        Defence = 1,
        SuppressAssault = 2,
        Flank = 3,
        Assault = 4,
        Pursuit = 5,
        Regroup = 6
    }

    public enum ISBOrderDirective
    {
        None = 0,
        Defence = 1,
        Suppress = 2,
        Flank = 3,
        Assault = 4,
        Pursuit = 5,
        Regroup = 6
    }

    public enum ISBOrderRole
    {
        Commander = 0,
        Support = 1,
        ManeuverLeft = 2,
        ManeuverRight = 3
    }
}
