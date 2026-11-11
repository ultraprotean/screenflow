namespace ScreenFlow.Domain;

public enum Industry { Transportation, Healthcare, Energy, Construction, Other }

public enum SearchType
{
    CriminalCounty,
    CriminalFederal,
    MotorVehicleRecord,
    DrugTest,
    EmploymentVerification,
    EducationVerification,
}

public enum OrderStatus { Submitted, InProgress, ReadyForReview, Completed, Cancelled }

public enum SearchStatus { Pending, InProgress, Completed, Cancelled }

/// <summary>FCRA vocabulary: "Consider" is a finding the employer must review, not a failure.</summary>
public enum ScreeningResult { Clear, Consider }

/// <summary>
/// The screening company's verification of an order's "Consider" findings before they are reported
/// (FCRA requires procedures for maximum possible accuracy). Confirm: the findings stand (right
/// person, reportable). Dismiss: they don't (a false match, or not reportable). This is not the
/// employer's hiring decision; employer adjudication is out of scope.
/// </summary>
public enum ReviewDecision { Confirm, Dismiss }
