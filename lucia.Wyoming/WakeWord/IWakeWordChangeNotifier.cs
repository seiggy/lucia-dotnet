namespace lucia.Wyoming.WakeWord;

public interface IWakeWordChangeNotifier
{
    event Action? KeywordsChanged;
}
