﻿namespace Sequencer.Types;

public class TypedIdentityEvt : ISeqEvt
{
    public required IdentityEvt Evt { get; init; }
    public TypedCommitType Type => TypedCommitType.Identity;
    public required int Seq { get; init; }
    public required DateTime Time { get; init; }
}