using System.Collections.Generic;

namespace PocketFootball.Sim
{
    // Where the painted marks go, in yards. Pure data so the view and the tests share it.
    public static class FieldLayout
    {
        // Yard lines from goal line to goal line (every lineEveryYd), then the two end lines.
        public static List<float> YardLineXs(FieldConfig f)
        {
            var xs = new List<float>();
            for (float x = -f.GoalLineX; x <= f.GoalLineX + f.lineWidthYd * 0.5f; x += f.lineEveryYd) xs.Add(x);
            xs.Add(-f.HalfLength);
            xs.Add(f.HalfLength);
            xs.Sort();
            return xs;
        }

        // The grass stripes between the goal lines: (startX, endX) pairs, alternating colours by index.
        public static List<float[]> Stripes(FieldConfig f)
        {
            var s = new List<float[]>();
            for (float x = -f.GoalLineX; x < f.GoalLineX - f.lineWidthYd; x += f.stripeYd)
                s.Add(new[] { x, x + f.stripeYd });
            return s;
        }

        // One short tick per yard on both hash rows, between the goal lines, skipping the yard lines themselves.
        public static List<float[]> HashMarks(FieldConfig f)
        {
            var marks = new List<float[]>();
            float z = f.HalfWidth - f.hashFromSidelineYd;
            int yards = (int)(f.GoalLineX + f.GoalLineX);
            for (int i = 1; i < yards; i++)
            {
                float x = -f.GoalLineX + i;
                if (i % (int)f.lineEveryYd == 0) continue;
                marks.Add(new[] { x, -z });
                marks.Add(new[] { x, z });
            }
            return marks;
        }
    }
}
