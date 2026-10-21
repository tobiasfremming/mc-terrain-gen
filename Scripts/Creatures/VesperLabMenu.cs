using UnityEngine;

// The VesperLab "M" menu's state, plain C# (VesperLab draws it with IMGUI; CreatureVesperLabTests drive it directly).
// Open/closed, the command last picked (and WalkTo's point), and the brain it commands. On a respawn the lab hands the
// new individual's CommandBrain to Attach, which gives it the picked command again: the menu (open or not) and the
// command survive the respawn. A WalkTo that has already arrived (its brain turned to Stand) is kept as Stand.
public sealed class VesperLabMenu
{
    // the menu's buttons, in order (keys 1-7 while the menu is open)
    public static readonly CreatureCommand[] Commands =
    {
        CreatureCommand.Stand, CreatureCommand.WalkAround, CreatureCommand.WalkTo, CreatureCommand.Fly,
        CreatureCommand.Circle, CreatureCommand.Land, CreatureCommand.Perch,
    };

    public const string Hint = "F3: skeleton    M: close menu    1-7: commands";
    public const string ClosedHint = "M: menu    F3: skeleton";

    public static string Label(CreatureCommand c)
    {
        switch (c)
        {
            case CreatureCommand.Stand: return "Stand";
            case CreatureCommand.WalkAround: return "Walk around";
            case CreatureCommand.WalkTo: return "Walk to me";
            case CreatureCommand.Fly: return "Fly";
            case CreatureCommand.Circle: return "Circle";
            case CreatureCommand.Land: return "Land";
            case CreatureCommand.Perch: return "Perch on a rock";
            default: return c.ToString();
        }
    }

    public bool Open { get; private set; }
    public CreatureCommand Selected { get; private set; } = CreatureCommand.Stand;
    public Vector3 Point { get; private set; }
    public CommandBrain Brain { get; private set; }

    // RED hook (tests): Attach does not give the new brain the picked command
    internal bool forgetOnAttach;

    public void Toggle() => Open = !Open;

    public void Pick(CreatureCommand c) => Pick(c, Point);

    // WalkTo's point: where to walk (the lab passes the player's ground point)
    public void Pick(CreatureCommand c, Vector3 point)
    {
        Selected = c; Point = point;
        if (Brain != null) Brain.Command(c, point);
    }

    // A (new) individual's brain: it gets the picked command (a respawn keeps what was asked).
    public void Attach(CommandBrain brain)
    {
        if (Brain != null && Selected == CreatureCommand.WalkTo && Brain.Arrived) Selected = CreatureCommand.Stand;
        Brain = brain;
        if (brain != null && !forgetOnAttach) brain.Command(Selected, Point);
    }

    string lastDoing, status = "Doing: -", closedText = ClosedHint;

    // "Doing: ..." (the brain's Doing is one of a set of constant strings: the line is rebuilt only when it changes)
    public string Status
    {
        get
        {
            string d = Brain != null ? Brain.Doing : "no Vesper";
            if (!ReferenceEquals(d, lastDoing)) { lastDoing = d; status = "Doing: " + d; closedText = ClosedHint + "\n" + status; }
            return status;
        }
    }

    // the closed menu's box: the hint and "Doing: ..." (rebuilt with Status)
    public string ClosedText { get { _ = Status; return closedText; } }

    // the open menu's title and button labels ("3  Walk to me", the picked one marked): rebuilt only when the
    // picked command changes
    readonly string[] labels = new string[Commands.Length];
    string title;
    CreatureCommand labelled = (CreatureCommand)(-1);

    public string Title { get { Relabel(); return title; } }
    public string ButtonLabel(int i) { Relabel(); return labels[i]; }

    void Relabel()
    {
        if (labelled == Selected) return;
        labelled = Selected;
        title = "Vesper  -  " + Label(Selected);
        for (int i = 0; i < Commands.Length; i++)
            labels[i] = (i + 1) + "  " + Label(Commands[i]) + (Commands[i] == Selected ? "  <" : "");
    }
}
