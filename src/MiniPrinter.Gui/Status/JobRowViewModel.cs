using MiniPrinter.Control;

namespace MiniPrinter.Gui;

/// <summary>The icon of a job in the list; the view draws each with a symbol and a colour of the theme.</summary>
public enum JobIcon
{
    Waiting,
    Printing,
    Done,
    Held,
    Failed,
    Canceled,
}

/// <summary>
/// A row of the list of jobs: the state and the message in the language of the interface, an icon, where it came from and what
/// can be done with it. The row is updated in place when the service reports a change, so the selection and the scroll stay.
/// </summary>
public sealed class JobRowViewModel : ObservableObject
{
    private JobDto _job;

    public JobRowViewModel(JobDto job)
    {
        _job = job;
    }

    public int Id => _job.Id;

    public string Name => _job.Name;

    public DateTimeOffset Created => _job.Created;

    public int Pages => _job.Pages;

    public string StateText => ServiceTexts.State(_job.State);

    public string MessageText => ServiceTexts.Message(_job.Message);

    public bool IsFinished => _job.State is "Completed" or "Canceled" or "Aborted";

    public JobIcon Icon => _job.State switch
    {
        "Completed" => JobIcon.Done,
        "Processing" => JobIcon.Printing,
        "ProcessingStopped" or "PendingHeld" => JobIcon.Held,
        "Aborted" => JobIcon.Failed,
        "Canceled" => JobIcon.Canceled,
        _ => JobIcon.Waiting,
    };

    /// <summary>Where the job came from: "Windows · maria", "Puerto 9100 · 192.168.0.30", "Panel"… (empty for a service that does not say).</summary>
    public string OriginText
    {
        get
        {
            var source = _job.Source switch
            {
                "Windows" => Strings.Get("Job.Source.Windows"),
                "Panel" => Strings.Get("Job.Source.Panel"),
                "Api" => Strings.Get("Job.Source.Api"),
                "Raw" => Strings.Get("Job.Source.Raw"),
                null or "" => "",
                var other => other,
            };
            return string.IsNullOrEmpty(_job.Origin) || _job.Source == "Panel" || string.IsNullOrEmpty(source) ? source : $"{source} · {_job.Origin}";
        }
    }

    public bool CanCancel => !IsFinished;

    /// <summary>The service kept the pages and the job is over: it can be printed again.</summary>
    public bool CanReprint => IsFinished && _job.CanReprint;

    /// <summary>Why the preview of this job is not available, or null when it is (or when it may be).</summary>
    public string? PreviewProblem
    {
        get
        {
            if (!IsFinished)
                return Strings.Get("Preview.NotFinished");
            if (_job.CanReprint)
                return null;
            return _job.Pages > MaxKeptPages ? Strings.Get("Preview.TooLarge", MaxKeptPages) : Strings.Get("Preview.NotKept");
        }
    }

    /// <summary>The service keeps at most this many pages of a job.</summary>
    public const int MaxKeptPages = 40;

    /// <summary>Takes the new state of the job; raises the changes of what shows.</summary>
    public void Update(JobDto job)
    {
        if (job == _job)
            return;
        _job = job;
        OnPropertyChanged(string.Empty);
    }

    public JobDto Dto => _job;
}
