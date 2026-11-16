namespace CorePlasma.Common.Constants;

public static class GasTypes
{
    public const string Oxygen = "Oxygen";
    public const string CO2 = "CO2";
    public const string Argon = "Argon";
    public const string Nitrogen = "Nitrogen";
    public const string LPG = "LPG";
    public const string Acetylene = "Acetylene";
    public const string MixedGas = "MixedGas";
    public const string Other = "Other";

    public static readonly IReadOnlyCollection<string> All =
    [
        Oxygen, CO2, Argon, Nitrogen, LPG, Acetylene, MixedGas, Other
    ];
}
