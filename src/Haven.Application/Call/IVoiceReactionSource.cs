using Haven.Core;

namespace Haven.Application;

public interface IVoiceReactionSource
{
    VoiceProfile? ActiveVoiceProfile { get; }
    VoiceReaction? LatestVoiceReaction { get; }
    event EventHandler<VoiceReactionEventArgs>? VoiceReactionChanged;
}
