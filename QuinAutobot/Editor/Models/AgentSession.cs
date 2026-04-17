using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace QuinAutobot
{
    public class AgentSession
    {
        private readonly List<LMMessage> _history = new List<LMMessage>();
        private int _maxHistoryMessages;

        public int MessageCount => _history.Count;

        public AgentSession(int maxHistoryMessages = 40)
        {
            _maxHistoryMessages = maxHistoryMessages;
        }

        public void AppendUser(string content)
        {
            _history.Add(new LMMessage("user", content));
            TrimIfNeeded();
        }

        public void AppendAssistant(string content)
        {
            _history.Add(new LMMessage("assistant", content));
        }

        public List<LMMessage> GetHistory() => _history.ToList();

        public void Clear() => _history.Clear();

        public void SetMaxHistory(int max)
        {
            _maxHistoryMessages = max;
            TrimIfNeeded();
        }

        private void TrimIfNeeded()
        {
            while (_history.Count > _maxHistoryMessages)
                _history.RemoveAt(0);
        }

        public string BuildContextSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Session messages: {_history.Count}");
            if (_history.Count > 0)
                sb.AppendLine($"Last role: {_history[^1].role}");
            return sb.ToString();
        }
    }
}
