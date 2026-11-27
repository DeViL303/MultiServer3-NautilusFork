namespace Nautilus2027
{
    public enum OverwriteBehavior
    {
        Overwrite,
        Rename,
        Skip
    }

    public enum ArchiveTypeSetting
    {
        BAR,
        BAR_S,
        SDAT,
        SDAT_SHARC,
        CORE_SHARC,
        CONFIG_SHARC
    }

    public enum CdnMode
    {
        RETAIL,
        BETA,
        HDK
    }

    public enum RememberLastTabUsed
    {
        ArchiveTool,
        CDSTool,
        HCDBTool,
        SQLTool,
        SceneList,
        TickLSTTool,
        TSS,
        SceneIDTool,
        LUACTool,
        SDCODCTool,
        Path2Hash,
        EbootPatcher,
        SHAChecker,
        Catalogue,
        MediaTool,
        ModelTool,
        AnimationTool,
        AtmosEffectsTool,
        EfxProbeTool,
        AgfRigTool
    }

    public enum ArchiveMapperSetting
    {
        NORM,
        FAST,
        CORE,
        EXP
    }

    public enum SaveDebugLog
    {
        True,
        False,
    }
}
