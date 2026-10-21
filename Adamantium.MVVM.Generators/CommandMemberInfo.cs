namespace Adamantium.MVVM.Generators;

/// <summary>Value-equatable model for a <c>[Command]</c> method's generated command property.
/// <see cref="CommandTypeFqn"/> is AdamantiumCommand (sync) or AdamantiumAsyncCommand (async);
/// <see cref="ExecuteExpression"/> is the prebuilt ctor delegate (e.g. <c>() => Save()</c> or <c>ct => Save(ct)</c>);
/// <see cref="CanExecuteExpression"/> is the CanExecute argument (e.g. <c>() => CanSave()</c>) or null.
/// A non-null <see cref="Diagnostic"/> is reported alongside the emit - the command is still generated, gateless.</summary>
internal sealed record CommandMemberInfo(
    string Namespace,
    string TypeKeyword,
    string TypeName,
    string CommandName,
    string FieldName,
    string CommandTypeFqn,
    string ExecuteExpression,
    string CanExecuteExpression,
    string HintName,
    string DescribedType,
    string PlainCommandType,
    DiagnosticInfo Diagnostic = null);
