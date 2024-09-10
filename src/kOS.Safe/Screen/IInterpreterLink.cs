using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace kOS.Safe.Screen
{
    public interface IInterpreterLink
    {
        void ProcessCommand(string commandText);
        bool IsCommandComplete(string commandText);
        bool IsWaitingForCommand();
        void BreakExecution(bool manual);
        int InstructionsThisUpdate();
    }
}
