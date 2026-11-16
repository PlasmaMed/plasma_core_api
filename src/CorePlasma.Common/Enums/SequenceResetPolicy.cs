namespace CorePlasma.Common.Enums;

/// <summary>
/// When the running number of a document sequence restarts at 1.
/// </summary>
public enum SequenceResetPolicy
{
    Never = 0,
    Yearly = 1,
    Monthly = 2,
    Daily = 3
}
