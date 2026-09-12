namespace OsuClient.Game.Beatmaps.HitObjects
{
    /// <summary>
    /// A plain hit circle.
    ///
    /// Record layout: <c>x,y,time,type,hitSound,hitSample</c>
    /// </summary>
    public class HitCircleData : HitObjectData
    {
        /// <summary>A circle is instantaneous.</summary>
        public override double EndTime => StartTime;

        public override string ToString() =>
            $"Circle @ {StartTime:0}ms ({X:0},{Y:0}){(NewCombo ? " [NC]" : string.Empty)}";
    }
}
