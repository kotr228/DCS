using JolieCat3D.Service.Animation;

namespace JolieCat3D.Service.Commands
{
    /// <summary>
    /// Undo for any NLA edit (add/move/delete strips and tracks, push down, strip
    /// settings): snapshots the NLA stack, the action list and the active action before
    /// and after. Actions themselves are shared, not copied - their keys are undone by
    /// the keyframe commands.
    /// </summary>
    public sealed class NlaStateCommand : IUndoableCommand
    {
        private sealed record State(List<NlaTrack> Tracks, List<AnimationAction> Actions, AnimationAction Active);

        private readonly Func<AnimationTimeline> _timeline;
        private readonly State _before;
        private readonly State _after;
        private readonly Action? _onChanged;

        private NlaStateCommand(Func<AnimationTimeline> timeline, State before, State after, string description, Action? onChanged)
        {
            _timeline = timeline;
            _before = before;
            _after = after;
            Description = description;
            _onChanged = onChanged;
        }

        public string Description { get; }

        /// <summary>Runs <paramref name="edit"/> on the timeline and returns a command that
        /// has already been applied (pass it to <c>CommandHistory.Record</c>), or null when
        /// <paramref name="edit"/> returned false (nothing changed).</summary>
        public static NlaStateCommand? Capture(Func<AnimationTimeline> timeline, string description, Func<AnimationTimeline, bool> edit, Action? onChanged = null)
        {
            var t = timeline();
            var before = Snapshot(t);
            if (!edit(t))
            {
                Restore(t, before);
                return null;
            }
            return new NlaStateCommand(timeline, before, Snapshot(t), description, onChanged);
        }

        private static State Snapshot(AnimationTimeline t) => new(t.CloneNla(), t.Actions.ToList(), t.ActiveAction);

        private static void Restore(AnimationTimeline t, State state)
        {
            t.RestoreActions(state.Actions, state.Active);
            t.RestoreNla(state.Tracks);
        }

        public void Execute()
        {
            Restore(_timeline(), _after);
            _onChanged?.Invoke();
        }

        public void Undo()
        {
            Restore(_timeline(), _before);
            _onChanged?.Invoke();
        }
    }
}
