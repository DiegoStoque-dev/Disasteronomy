#nullable enable
using Godot;

/// <summary>
/// End-of-mission performance ranking (F to S) for Phase 1.
///
/// Score combines:
///   - completion time        (faster = better)
///   - tether fatigue strikes (fewer = better, rupture is a heavy penalty)
///   - remaining SAFER fuel   (more saved = bonus)
///
/// Weights and grade thresholds are centralized here so you can tweak the
/// balance without touching the rest of the game.
/// </summary>
public static class MissionRank
{
    // --- Score weights (tuned manually, feel free to adjust) ---
    public const float TimeBaseScore = 1000f;        // score for an instant mission
    public const float TimeWeight = 800f;            // subtracted per second elapsed
    public const float StressPenaltyPerStrike = 160f; // normal fatigue strike
    public const float RupturePenalty = 600f;        // tether snapped (much heavier)
    public const float SaferWeightPerCharge = 130f;  // bonus per remaining charge

    // --- Grade thresholds (higher is better) ---
    public const float GradeS = 700f;
    public const float GradeA = 560f;
    public const float GradeB = 430f;
    public const float GradeC = 300f;
    public const float GradeD = 160f;
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
        double score = TimeBaseScore
                     - TimeWeight * Mathf.Max(0f, completionTimeSeconds)
                     - StressPenaltyPerStrike * Mathf.Max(0, stressStrikes)
                     - (ruptured ? RupturePenalty : 0f)
                     + SaferWeightPerCharge * Mathf.Max(0, remainingSafer);

        float clamped = Mathf.Max(0f, (float)score);
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
