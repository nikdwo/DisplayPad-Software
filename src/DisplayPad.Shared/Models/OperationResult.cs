namespace DisplayPad.Shared.Models;

public enum OperationErrorCode
{
    None,
    Unknown,
    LocalUnsupportedAction,
    ObsNotConnected,
    ObsSceneMissing,
    ObsSourceMissing,
    ObsHotkeyMissing,
    ObsFilterMissing,
    ObsUnknownCommand,
    ObsConnectionFailed,
    ObsConnectionTimeout,
    NvidiaFunctionMissing,
    NvidiaConfigMissing,
    NvidiaBindingMissing,
    BaseCampDesiredStateNotReached,
    BaseCampServiceNotRunning,
    AdminProcessNotStarted,
    AdminProcessTimeout,
    AdminProcessExitCode,
    AdminRightsDenied,
    RemoteMissingFingerprint,
    RemoteHttpError,
    RemoteNetworkError,
    AgentTooManyAuthenticationFailures,
    AgentInvalidAuthentication,
    AgentUnsupportedAction,
    AgentMissingHotkey,
    AgentMissingCommand,
    AgentExecutionFailed,
    ConfigProfileRequired,
    ConfigActiveProfileInvalid,
    ConfigPortInvalid,
    ConfigKeyMatrixInvalid,
    ConfigLanguageInvalid,
    ConfigNullProfile,
    ConfigPageRequired,
    ConfigNullPage,
    ConfigFolderTooDeep,
    ConfigNullKeys,
    ConfigDuplicateKeys,
    ConfigKeyIndexInvalid,
    ConfigKeysIncomplete,
    ConfigNullAction,
    ConfigBackKeyReserved,
    ConfigFolderTargetRequired,
    ConfigUnexpectedFolderPage,
    ConfigFieldTooLong,
    ConfigTooLarge,
    ConfigVersionUnsupported,
    ConfigRecoveredFromBackup,
    ConfigLoadFailed,
    ConfigAndBackupInvalid
}

public sealed record OperationResult(
    OperationErrorCode ErrorCode = OperationErrorCode.None,
    string? TechnicalDetail = null,
    params string[] Parameters)
{
    public bool Success => ErrorCode == OperationErrorCode.None;

    public static OperationResult Ok() => new();

    public static OperationResult Fail(OperationErrorCode errorCode, string? technicalDetail = null,
        params string[] parameters) => new(errorCode, technicalDetail, parameters);
}

public interface IConfigNameProvider
{
    string ProfileName(string language, int number);
    string PageName(string language, int number);
}
