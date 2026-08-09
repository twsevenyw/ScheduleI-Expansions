using UnityEngine;

namespace Expansions.Core.Tutorial;

/// <summary>
/// The <see cref="ITutorialChapterBuilder"/> the director hands to a chapter. Step ids are prefixed
/// with the chapter id, so two mods can both declare a step called <c>open</c> without colliding in
/// <see cref="TutorialSignals"/>.
/// </summary>
internal sealed class TutorialChapterBuilder : ITutorialChapterBuilder
{
    private readonly string _chapterId;
    private readonly List<StepBuilder> _steps = new();

    internal TutorialChapterBuilder(string chapterId) => _chapterId = chapterId;

    public ITutorialStepBuilder AddStep(string id, string title)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Step id must be a non-empty, stable string.", nameof(id));

        var step = new StepBuilder($"{_chapterId}.{id}", string.IsNullOrWhiteSpace(title) ? id : title);
        _steps.Add(step);
        return step;
    }

    /// <summary>Materialises the steps, dropping duplicates and anything that can never complete.</summary>
    internal IReadOnlyList<TutorialStep> Build()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var built = new List<TutorialStep>(_steps.Count);

        foreach (var builder in _steps)
        {
            var step = builder.ToStep();

            if (!seen.Add(step.Id))
            {
                ExpansionHost.Log.Warn(
                    $"Tutorial chapter '{_chapterId}' declared step '{step.Id}' twice; keeping the first.");
                continue;
            }

            if (step.Condition is null && !step.CompletesOnSignal)
            {
                ExpansionHost.Log.Warn(
                    $"Tutorial step '{step.Id}' has neither a condition nor a signal, so it could never " +
                    $"tick off and would stall the quest line; skipping it.");
                continue;
            }

            built.Add(step);
        }

        return built;
    }

    private sealed class StepBuilder : ITutorialStepBuilder
    {
        private readonly string _id;
        private readonly string _title;

        private string _hint = string.Empty;
        private string _rewardDescription = string.Empty;
        private Vector3? _poi;
        private Func<bool>? _condition;
        private Action? _reward;
        private bool _signal;

        internal StepBuilder(string id, string title)
        {
            _id = id;
            _title = title;
        }

        public ITutorialStepBuilder Describe(string hint)
        {
            _hint = hint ?? string.Empty;
            return this;
        }

        public ITutorialStepBuilder CompletesWhen(Func<bool> condition)
        {
            _condition = condition ?? throw new ArgumentNullException(nameof(condition));
            return this;
        }

        public ITutorialStepBuilder CompletesOnSignal()
        {
            _signal = true;
            return this;
        }

        public ITutorialStepBuilder At(Vector3 poi)
        {
            _poi = poi;
            return this;
        }

        public ITutorialStepBuilder Rewards(string description, Action grant)
        {
            _rewardDescription = description ?? string.Empty;
            _reward = grant;
            return this;
        }

        internal TutorialStep ToStep() =>
            new(_id, _title, _hint, _poi, _condition, _signal, _rewardDescription, _reward);
    }
}
