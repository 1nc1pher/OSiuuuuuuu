using osuTK.Input;

namespace OsuClient.Game.Input
{
    /// <summary>
    /// Which of the two hit keys an input belongs to. Both do exactly the same
    /// thing to gameplay — the distinction only exists so the key overlay can
    /// show each key's own presses and timing.
    /// </summary>
    public enum HitKeyColumn
    {
        First,
        Second,
    }

    /// <summary>osu!'s default gameplay hit keys.</summary>
    public static class GameplayKeyBindings
    {
        /// <summary>Which key column a keyboard key drives, or null if it isn't a hit key.</summary>
        public static HitKeyColumn? ColumnFor(Key key) => key switch
        {
            Key.Z => HitKeyColumn.First,
            Key.X => HitKeyColumn.Second,
            _ => null,
        };

        /// <summary>Which key column a mouse button drives — the buttons hit as well, as in osu!.</summary>
        public static HitKeyColumn? ColumnFor(MouseButton button) => button switch
        {
            MouseButton.Left => HitKeyColumn.First,
            MouseButton.Right => HitKeyColumn.Second,
            _ => null,
        };
    }
}
