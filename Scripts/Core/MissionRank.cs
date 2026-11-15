#nullable enable
using Godot;

/// <summary>
/// End-of-mission performance ranking (F to S) for Phase 1.
///
/// Score combines:
///   - completion time        (compared to a "par time", not a flat per-second drain,
///                             so a normal-length mission can still reach a good grade)
///   - tether fatigue strikes (fewer = better, rupture is a heavy penalty)
///   - remaining SAFER fuel   (more saved = bonus)
///
/// Weights and grade thresholds are centralized here so you can tweak the
/// balance without touching the rest of the game.
/// </summary>
public static class MissionRank
{
    // --- Score weights (tuned manually, feel free to adjust) ---
    public const float TimeBaseScore = 400f;          // score when the mission takes exactly ParTimeSeconds
    public const float ParTimeSeconds = 150f;         // target EVA time (2:30); running faster scales it up
    public const float MaxTimeRatio = 2f;             // cap on the speed bonus (never more than 2x base)
    public const float StressPenaltyPerStrike = 120f; // normal fatigue strike
    public const float RupturePenalty = 350f;         // tether snapped (heavy, on top of its strikes)
    public const float SaferWeightPerCharge = 80f;    // bonus per remaining charge

    // --- Grade thresholds (higher is better) ---
    public const float GradeS = 850f;
    public const float GradeA = 700f;
    public const float GradeB = 550f;
    public const float GradeC = 400f;
    public const float GradeD = 250f;
    // Below GradeD => F

    /// <summary>Result of a single mission run.</summary>
    public readonly struct Result
    {
        public readonly float Score;
        public readonly char Grade; // 'S','A','B','C','D','F'

        public Result(float score, char grade)
        {
            Score = score;
            Grade = grade;
        }
    }

    /// <summary>
    /// Computes the score and grade for one player's run.
    /// </summary>
    /// <param name="completionTimeSeconds">Total mission duration in seconds.</param>
    /// <param name="stressStrikes">How many times the tether reached critical fatigue.</param>
    /// <param name="ruptured">Whether the tether snapped during the mission.</param>
    /// <param name="remainingSafer">SAFER charges still available at the end.</param>
    public static Result Compute(
        float completionTimeSeconds,
        int stressStrikes,
        bool ruptured,
        int remainingSafer)
    {
        // Time is scaled around the par time: finish under it and score grows towards the cap,
        // finish over it and score shrinks - but it never collapses instantly like a flat drain.
        float timeRatio = Mathf.Clamp(ParTimeSeconds / Mathf.Max(1f, completionTimeSeconds), 0f, MaxTimeRatio);

        float score = TimeBaseScore * timeRatio
                    - StressPenaltyPerStrike * Mathf.Max(0, stressStrikes)
                    - (ruptured ? RupturePenalty : 0f)
                    + SaferWeightPerCharge * Mathf.Max(0, remainingSafer);

        float clamped = Mathf.Max(0f, score);
        char grade = GradeFromScore(clamped);
        return new Result(clamped, grade);
    }

    /// <summary>Maps a raw score to a grade letter.</summary>
    public static char GradeFromScore(float score)
    {
        if (score >= GradeS) return 'S';
        if (score >= GradeA) return 'A';
        if (score >= GradeB) return 'B';
        if (score >= GradeC) return 'C';
        if (score >= GradeD) return 'D';
        return 'F';
    }
}
