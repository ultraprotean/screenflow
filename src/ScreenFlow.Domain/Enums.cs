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
