using System;
using kOS.Safe.Encapsulation;
using kOS.Safe.Encapsulation.Suffixes;
using kOS.Safe.Execution;
using kOS.Safe.Function;
using Debug = UnityEngine.Debug;

namespace kOS.Lua.Types
{
    public class KSFunction : LuaTypeBase
    {
        private static readonly Type[] bindingTypes = { typeof(SafeFunctionBase), typeof(DelegateSuffixResult) };
        public override string MetatableName => "KerboscriptFunction";
        public override Type[] BindingTypes => bindingTypes;

        public KSFunction(KeraLua.Lua state)
        {
            state.NewMetaTable(MetatableName);
            state.PushString("__type");
            state.PushString(MetatableName);
            state.RawSet(-3);
            state.PushString("__call");
            state.PushCFunction(KSFunctionCall);
            state.RawSet(-3);
            state.PushString("__gc");
            state.PushCFunction(Binding.CollectObject);
            state.RawSet(-3);
            state.PushString("__tostring");
            state.PushCFunction(Binding.ObjectToString);
            state.RawSet(-3);
        }

        private static int KSFunctionCall(IntPtr L)
        {
            var state = KeraLua.Lua.FromIntPtr(L);
            var binding = Binding.bindings[state.MainThread.Handle];
            var ksFunction = binding.Objects[state.ToUserData(1)];
            
            var stack = (binding.Shared.Cpu as LuaCPU).Stack;
            stack.Clear();
            stack.PushArgument(new KOSArgMarkerType());
            for (int i = 2; i <= state.GetTop(); i++)
                stack.PushArgument(Binding.ToCSharpObject(state, i, binding));
            
            if (ksFunction is SafeFunctionBase function)
            {
                try { function.Execute(binding.Shared); }
                catch (Exception e) { Debug.Log(e); return state.Error(e.Message); }
                return Binding.PushLuaType(state, Structure.ToPrimitive(function.ReturnValue), binding);
            }
            if (ksFunction is DelegateSuffixResult delegateResult)
            {
                try { delegateResult.Invoke(binding.Shared.Cpu); }
                catch (Exception e) { Debug.Log(e); return state.Error(e.Message); }
                return Binding.PushLuaType(state, Structure.ToPrimitive(delegateResult.Value), binding);
            }
            return state.Error(string.Format("attempt to call a non function {0} value", ksFunction.GetType().Name));
        }
    }
}
