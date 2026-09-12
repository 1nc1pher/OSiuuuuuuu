namespace OsuClient.Game.Beatmaps.HitObjects
{
    /// <summary>
    /// A spinner. Unlike a slider, a spinner carries its end time explicitly
    /// in the record, so no timing maths is needed to resolve its duration.
    ///
    /// Record layout: <c>x,y,time,type,hitSound,endTime,hitSample</c>
    ///
    /// The backend always centres spinners on the playfield.
    /// </summary>
    public class SpinnerData : HitObjectData
    {
        private double endTime;

        public override double EndTime => endTime;

        public void SetEndTime(double value) => endTime = value;

        public override string ToString() =>
            $"Spinner @ {StartTime:0}-{EndTime:0}ms{(NewCombo ? " [NC]" : string.Empty)}";
    }
}
