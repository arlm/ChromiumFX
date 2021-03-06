// Copyright (c) 2014-2017 Wolfgang Borgsmüller
// All rights reserved.
// 
// This software may be modified and distributed under the terms
// of the BSD license. See the License.txt file for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

public class CfxClientClass : CfxClass {


    public static void DefineEventHandlerNames(CefStructType[] structTypes) {

        // collect all callback functions in a dictionary with the public name as key

        var events = new Dictionary<string, List<CefCallbackFunction>>();

        foreach(var st in structTypes) {
            if(st.Category == StructCategory.Client) {
                foreach(var cb in st.ClassBuilder.CallbackFunctions) {
                    var key = cb.PublicName;
                    if(!events.TryGetValue(key, out List<CefCallbackFunction> list)) {
                        list = new List<CefCallbackFunction>();
                        events.Add(key, list);
                    }
                    list.Add(cb);
                }
            }
        }

        // define event names:
        // for public names with a single callback function:
        //    for basic events (no arguments, return type void), don't set the event name
        //    for non-basic events, use the public name prefixed with "Cfx"
        // for public names with more than one callback function:
        //    for basic events, don't set the event name
        //    for non-basic events:
        //        if all callback functions have the same comments, 
        //        then use the public name prefixed with "Cfx"
        //        else, concatenate the parent's class name with the public name

        foreach(var list in events.Values) {
            if(list.Count > 1) {
                var allDuplicates = true;
                var cb0 = list[0];
                for(int i = 1; i < list.Count && allDuplicates; ++i) {
                    var cb1 = list[i];
                    if(cb0.Comments.Lines.Length != cb1.Comments.Lines.Length) {
                        allDuplicates = false;
                    } else {
                        for(int ii = 0; ii < cb0.Comments.Lines.Length && allDuplicates; ++ii) {
                            if(cb0.Comments.Lines[ii] != cb1.Comments.Lines[ii]) {
                                allDuplicates = false;
                            }
                        }
                    }
                }
                for(int i = 0; i < list.Count; ++i) {
                    var cb = list[i];
                    if(cb.Signature.ManagedParameters.Length == 1 && cb.Signature.PublicReturnType.IsVoid) {
                        // a basic event, don't set event name
                    } else if(allDuplicates) {
                        cb.EventName = "Cfx" + cb.PublicName;
                    } else {
                        cb.EventName = cb.Parent.ClassName + cb.PublicName;
                    }
                }
            } else {
                var cb = list[0];
                if(cb.PublicName.Length < 4) {
                    // Special case, for short functions like "get" or "set", prepend the parent name
                    cb.EventName = cb.Parent.ClassName + cb.PublicName;
                } else if(cb.Signature.ManagedParameters.Length == 1 && cb.Signature.PublicReturnType.IsVoid) {
                    // a basic event, don't set event name
                } else {
                    cb.EventName = "Cfx" + cb.PublicName;
                } 
            }
        }
    }

    public override StructCategory Category {
        get {
            return StructCategory.Client;
        }
    }

    public CfxClientClass(CefStructType cefStruct, Parser.CallbackStructNode s, ApiTypeBuilder api)
        : base(cefStruct, s.Comments) {
        GetCallbackFunctions(s, api);
    }

    public override void EmitNativeWrapper(CodeBuilder b) {

        b.AppendComment(CefStruct.Name);
        b.AppendLine();

        b.BeginBlock("typedef struct _{0}", CfxNativeSymbol);
        b.AppendLine("{0} {1};", OriginalSymbol, CefStruct.Name);
        b.AppendLine("unsigned int ref_count;");
        b.AppendLine("gc_handle_t gc_handle;");
        b.AppendLine("int wrapper_kind;");
        b.AppendLine("// managed callbacks");
        foreach(var cb in CallbackFunctions) {
            b.AppendLine("void (CEF_CALLBACK *{0})({1});", cb.Name, cb.Signature.NativeParameterList);
        }
        b.EndBlock("{0};", CfxNativeSymbol);
        b.AppendLine();

        b.BeginBlock("void CEF_CALLBACK _{0}_add_ref(struct _cef_base_ref_counted_t* base)", CfxName);
        if(GeneratorConfig.UseStrongHandleFor(CefStruct.Name)) {
            b.AppendLine("int count = InterlockedIncrement(&(({0}*)base)->ref_count);", CfxNativeSymbol);
            b.BeginIf("count == 2");
            b.BeginIf("(({0}*)base)->wrapper_kind == 0", CfxNativeSymbol);
            b.AppendLine("cfx_gc_handle_switch(&(({0}*)base)->gc_handle, GC_HANDLE_UPGRADE);", CfxNativeSymbol);
            b.BeginElse();
            b.AppendLine("cfx_gc_handle_switch(&(({0}*)base)->gc_handle, GC_HANDLE_UPGRADE | GC_HANDLE_REMOTE);", CfxNativeSymbol);
            b.EndBlock();
            b.EndBlock();
        } else {
            b.AppendLine("InterlockedIncrement(&(({0}*)base)->ref_count);", CfxNativeSymbol);
        }
        b.EndBlock();
        b.BeginBlock("int CEF_CALLBACK _{0}_release(struct _cef_base_ref_counted_t* base)", CfxName);
        b.AppendLine("int count = InterlockedDecrement(&(({0}*)base)->ref_count);", CfxNativeSymbol);
        b.BeginIf("count == 0");
        b.BeginIf("(({0}*)base)->wrapper_kind == 0", CfxNativeSymbol);
        b.AppendLine("cfx_gc_handle_switch(&(({0}*)base)->gc_handle, GC_HANDLE_FREE);", CfxNativeSymbol);
        b.BeginElse();
        b.AppendLine("cfx_gc_handle_switch(&(({0}*)base)->gc_handle, GC_HANDLE_FREE | GC_HANDLE_REMOTE);", CfxNativeSymbol);
        b.EndBlock();
        b.AppendLine("free(base);");
        b.AppendLine("return 1;");
        b.EndBlock();
        if(GeneratorConfig.UseStrongHandleFor(CefStruct.Name)) {
            b.BeginIf("count == 1");
            b.BeginIf("(({0}*)base)->wrapper_kind == 0", CfxNativeSymbol);
            b.AppendLine("cfx_gc_handle_switch(&(({0}*)base)->gc_handle, GC_HANDLE_DOWNGRADE);", CfxNativeSymbol);
            b.BeginElse();
            b.AppendLine("cfx_gc_handle_switch(&(({0}*)base)->gc_handle, GC_HANDLE_DOWNGRADE | GC_HANDLE_REMOTE);", CfxNativeSymbol);
            b.EndBlock();
            b.EndBlock();
        }
        b.AppendLine("return 0;");
        b.EndBlock();
        b.BeginBlock("int CEF_CALLBACK _{0}_has_one_ref(struct _cef_base_ref_counted_t* base)", CfxName);
        b.AppendLine("return (({0}*)base)->ref_count == 1 ? 1 : 0;", CfxNativeSymbol);
        b.EndBlock();
        b.AppendLine();

        b.BeginBlock("static {0}* {1}_ctor(gc_handle_t gc_handle, int wrapper_kind)", CfxNativeSymbol, CfxName);
        b.AppendLine("{0}* ptr = ({0}*)calloc(1, sizeof({0}));", CfxNativeSymbol);
        b.AppendLine("if(!ptr) return 0;");
        b.AppendLine("ptr->{0}.base.size = sizeof({1});", CefStruct.Name, OriginalSymbol);
        b.AppendLine("ptr->{0}.base.add_ref = _{1}_add_ref;", CefStruct.Name, CfxName);
        b.AppendLine("ptr->{0}.base.release = _{1}_release;", CefStruct.Name, CfxName);
        b.AppendLine("ptr->{0}.base.has_one_ref = _{1}_has_one_ref;", CefStruct.Name, CfxName);
        b.AppendLine("ptr->ref_count = 1;");
        b.AppendLine("ptr->gc_handle = gc_handle;");
        b.AppendLine("ptr->wrapper_kind = wrapper_kind;");
        b.AppendLine("return ptr;");
        b.EndBlock();
        b.AppendLine();

        if(NeedsWrapFunction) {
            b.BeginBlock("static gc_handle_t {0}_get_gc_handle({1}* self)", CfxName, CfxNativeSymbol);
            b.AppendLine("return self->gc_handle;");
            b.EndBlock();
            b.AppendLine();
        }

        foreach(var cb in CallbackFunctions) {

            b.AppendLine("// {0}", cb);
            b.AppendLine();

            b.BeginBlock("{0} CEF_CALLBACK {1}({2})", cb.NativeReturnType.OriginalSymbol, cb.NativeCallbackName, cb.Signature.OriginalParameterList);
            if(!cb.NativeReturnType.IsVoid) {
                cb.NativeReturnType.EmitNativeCallbackReturnValueFields(b);
            }

            foreach(var arg in cb.Signature.Parameters) {
                arg.EmitPreNativeCallbackStatements(b);
            }

            b.AppendLine("(({0}_t*)self)->{1}({2});", CfxName, cb.Name, cb.Signature.NativeArgumentList);

            foreach(var arg in cb.Signature.Parameters) {
                arg.EmitPostNativeCallbackStatements(b);
            }

            cb.NativeReturnType.EmitNativeCallbackReturnStatements(b);

            b.EndBlock();
            b.AppendLine();

        }

        b.BeginBlock("static void {0}_set_callback({1}* self, int index, void* callback)", CfxName, OriginalSymbol);
        b.BeginBlock("switch(index)");
        var index = 0;
        foreach(var cb in CallbackFunctions) {
            b.DecreaseIndent();
            b.AppendLine("case {0}:", index);
            b.IncreaseIndent();
            b.AppendLine("(({0}_t*)self)->{1} = (void (CEF_CALLBACK *)({2}))callback;", CfxName, cb.Name, cb.Signature.NativeParameterList);
            b.AppendLine("self->{0} = callback ? {1} : 0;", cb.Name, cb.NativeCallbackName);
            b.AppendLine("break;");
            cb.ClientCallbackIndex = index;
            index += 1;
        }
        b.EndBlock();
        b.EndBlock();

        b.AppendLine();
    }

    protected override void EmitApiDeclarations(CodeBuilder b) {
        if(Category == StructCategory.Client) {
            b.AppendLine("public static cfx_ctor_with_gc_handle_delegate {0}_ctor;", CfxName);
            if(NeedsWrapFunction) {
                b.AppendLine("public static cfx_get_gc_handle_delegate {0}_get_gc_handle;", CfxName);
            }
            b.AppendLine("public static cfx_set_callback_delegate {0}_set_callback;", CfxName);
            b.AppendLine();
        }
    }

    public override void EmitPublicClass(CodeBuilder b) {

        b.AppendLine("using Event;");
        b.AppendLine();

        b.AppendSummaryAndRemarks(Comments, false, true);

        b.BeginClass(ClassName + " : CfxBaseClient", GeneratorConfig.ClassModifiers(ClassName));
        b.AppendLine();

        if(NeedsWrapFunction) {
            b.BeginFunction("Wrap", ClassName, "IntPtr nativePtr", "internal static");
            b.AppendLine("if(nativePtr == IntPtr.Zero) return null;");
            b.AppendLine("var handlePtr = CfxApi.{0}.{1}_get_gc_handle(nativePtr);", ApiClassName, CfxName);
            b.AppendLine("return ({0})System.Runtime.InteropServices.GCHandle.FromIntPtr(handlePtr).Target;", ClassName);
            b.EndBlock();
            b.AppendLine();
            b.AppendLine();
        }

        b.AppendLine("private static object eventLock = new object();");
        b.AppendLine();

        b.BeginBlock("internal static void SetNativeCallbacks()");

        foreach(var sm in CallbackFunctions) {
            b.AppendLine("{0}_native = {0};", sm.Name);
        }
        b.AppendLine();
        foreach(var sm in CallbackFunctions) {
            b.AppendLine("{0}_native_ptr = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate({0}_native);", sm.Name);
        }

        b.EndBlock();
        b.AppendLine();

        foreach(var cb in CallbackFunctions) {

            var sig = cb.Signature;

            b.AppendComment(cb.ToString());
            CodeSnippets.EmitPInvokeCallbackDelegate(b, cb.Name, cb.Signature);
            b.AppendLine("private static {0}_delegate {0}_native;", cb.Name);
            b.AppendLine("private static IntPtr {0}_native_ptr;", cb.Name);
            b.AppendLine();

            b.BeginFunction(cb.Name, "void", sig.PInvokeParameterList, "internal static");
            //b.AppendLine("var handle = System.Runtime.InteropServices.GCHandle.FromIntPtr(gcHandlePtr);")
            b.AppendLine("var self = ({0})System.Runtime.InteropServices.GCHandle.FromIntPtr(gcHandlePtr).Target;", ClassName);
            b.BeginIf("self == null || self.CallbacksDisabled");
            if(!sig.ReturnType.IsVoid) {
                sig.ReturnType.EmitSetCallbackReturnValueToDefaultStatements(b);
            }
            foreach(var arg in sig.Parameters) {
                if(!arg.IsThisArgument)
                    arg.ParameterType.EmitSetCallbackArgumentToDefaultStatements(b, arg.VarName);
            }
            b.AppendLine("return;");
            b.EndBlock();
            b.AppendLine("var e = new {0}();", cb.PublicEventArgsClassName);
            for(var i = 1; i <= sig.ManagedParameters.Count() - 1; i++) {
                if(sig.ManagedParameters[i].ParameterType.IsIn) {
                    sig.ManagedParameters[i].EmitPublicEventFieldInitializers(b);
                }
            }
            b.AppendLine("self.m_{0}?.Invoke(self, e);", cb.PublicName);
            b.AppendLine("e.m_isInvalid = true;");

            for(var i = 1; i <= sig.ManagedParameters.Count() - 1; i++) {
                sig.ManagedParameters[i].EmitPostPublicRaiseEventStatements(b);
            }

            sig.EmitPostPublicEventHandlerReturnValueStatements(b);

            b.EndBlock();
            b.AppendLine();
        }

        if(NeedsWrapFunction) {
            b.AppendLine("internal {0}(IntPtr nativePtr) : base(nativePtr) {{}}", ClassName);
        }
        b.AppendLine("public {0}() : base(CfxApi.{1}.{2}_ctor) {{}}", ClassName, ApiClassName, CfxName);
        b.AppendLine();

        var cbIndex = 0;
        foreach(var cb in CallbackFunctions) {
            EmitPublicEvent(b, cbIndex, cb);
            b.AppendLine();
            cbIndex += 1;
        }

        var onlyBasicEvents = true;

        b.BeginFunction("OnDispose", "void", "IntPtr nativePtr", "internal override");
        cbIndex = 0;
        foreach(var cb in CallbackFunctions) {
            onlyBasicEvents &= cb.IsBasicEvent;
            b.BeginIf("m_{0} != null", cb.PublicName);
            b.AppendLine("m_{0} = null;", cb.PublicName);
            b.AppendLine("CfxApi.{0}.{1}_set_callback(NativePtr, {2}, IntPtr.Zero);", ApiClassName, CfxName, cbIndex);
            b.EndBlock();
            cbIndex += 1;
        }
        b.AppendLine("base.OnDispose(nativePtr);");
        b.EndBlock();

        b.EndBlock();

        if(!onlyBasicEvents) {

            b.AppendLine();
            b.AppendLine();

            b.BeginBlock("namespace Event");
            b.AppendLine();

            foreach(var cb in CallbackFunctions) {
                EmitPublicEventArgsAndHandler(b, cb);
                b.AppendLine();
            }

            b.EndBlock();
        }
    }

    private void EmitPublicEvent(CodeBuilder b, int cbIndex, CefCallbackFunction cb) {

        var isSimpleGetterEvent = cb.Signature.ManagedParameters.Length == 1
            && cb.Signature.ReturnType.IsCefStructPtrType;

        b.AppendSummaryAndRemarks(cb.Comments, false, true);
        b.BeginBlock("public event {0} {1}", cb.EventHandlerName, CSharp.Escape(cb.PublicName));
        b.BeginBlock("add");
        b.BeginBlock("lock(eventLock)");
        if(isSimpleGetterEvent) {
            b.BeginBlock("if(m_{0} != null)", cb.PublicName);
            b.AppendLine("throw new CfxException(\"Can't add more than one event handler to this type of event.\");");
            b.EndBlock();
        } else {
            b.BeginBlock("if(m_{0} == null)", cb.PublicName);
        }
        b.AppendLine("CfxApi.{3}.{0}_set_callback(NativePtr, {1}, {2}_native_ptr);", CefStruct.CfxName, cbIndex, cb.Name, CefStruct.ClassName.Substring(3));
        if(!isSimpleGetterEvent) b.EndBlock();
        b.AppendLine("m_{0} += value;", cb.PublicName);
        b.EndBlock();
        b.EndBlock();
        b.BeginBlock("remove");
        b.BeginBlock("lock(eventLock)");
        b.AppendLine("m_{0} -= value;", cb.PublicName);
        b.BeginBlock("if(m_{0} == null)", cb.PublicName);
        b.AppendLine("CfxApi.{2}.{0}_set_callback(NativePtr, {1}, IntPtr.Zero);", CefStruct.CfxName, cbIndex, CefStruct.ClassName.Substring(3));
        b.EndBlock();
        b.EndBlock();
        b.EndBlock();
        b.EndBlock();
        b.AppendLine();

        if(isSimpleGetterEvent) {
            b.AppendLine("/// <summary>");
            b.AppendLine("/// Retrieves the {0} provided by the event handler attached to the {1} event, if any.", cb.Signature.ReturnType.PublicSymbol, CSharp.Escape(cb.PublicName));
            b.AppendLine("/// Returns null if no event handler is attached.");
            b.AppendLine("/// </summary>");
            b.BeginBlock("public {0} Retrieve{1}()", cb.Signature.ReturnType.PublicSymbol, cb.Signature.ReturnType.PublicSymbol.Substring(3));
            b.AppendLine("var h = m_{0};", cb.PublicName);
            b.BeginIf("h != null");
            b.AppendLine("var e = new {0}();", cb.PublicEventArgsClassName);
            b.AppendLine("h(this, e);");
            b.AppendLine("return e.m_returnValue;");
            b.BeginElse();
            b.AppendLine("return null;");
            b.EndBlock();
            b.EndBlock();
            b.AppendLine();
        }

        b.AppendLine("private {0} m_{1};", cb.EventHandlerName, cb.PublicName);
    }

    private static HashSet<string> emittedEventHandlers = new HashSet<string>();

    private void EmitPublicEventArgsAndHandler(CodeBuilder b, CefCallbackFunction cb) {

        if(cb.IsBasicEvent)
            return;

        if(emittedEventHandlers.Contains(cb.EventHandlerName)) return;
        emittedEventHandlers.Add(cb.EventHandlerName);

        b.AppendSummaryAndRemarks(cb.Comments, false, true);
        b.AppendLine("public delegate void {0}(object sender, {1} e);", cb.EventHandlerName, cb.PublicEventArgsClassName);
        b.AppendLine();

        b.AppendSummaryAndRemarks(cb.Comments, false, true);
        b.BeginClass(cb.PublicEventArgsClassName + " : CfxEventArgs", GeneratorConfig.ClassModifiers(cb.PublicEventArgsClassName));
        b.AppendLine();

        for(var i = 1; i <= cb.Signature.ManagedParameters.Count() - 1; i++) {
            cb.Signature.ManagedParameters[i].EmitPublicEventArgFields(b);
        }
        b.AppendLine();

        if(!cb.Signature.PublicReturnType.IsVoid) {
            b.AppendLine("internal {0} m_returnValue;", cb.Signature.PublicReturnType.PublicSymbol);
            b.AppendLine("private bool returnValueSet;");
            b.AppendLine();
        }

        b.AppendLine("internal {0}() {{}}", cb.PublicEventArgsClassName);
        b.AppendLine();

        for(var i = 1; i <= cb.Signature.ManagedParameters.Count() - 1; i++) {
            var arg = cb.Signature.ManagedParameters[i];
            var cd = new CommentNode();
            if(arg.ParameterType.IsIn && arg.ParameterType.IsOut) {
                cd.Lines = new string[] { string.Format("Get or set the {0} parameter for the <see cref=\"{1}.{2}\"/> callback.", arg.PublicPropertyName, ClassName, cb.PublicName) };
            } else if(arg.ParameterType.IsIn) {
                cd.Lines = new string[] { string.Format("Get the {0} parameter for the <see cref=\"{1}.{2}\"/> callback.", arg.PublicPropertyName, ClassName, cb.PublicName) };
            } else {
                cd.Lines = new string[] { string.Format("Set the {0} out parameter for the <see cref=\"{1}.{2}\"/> callback.", arg.PublicPropertyName, ClassName, cb.PublicName) };
            }
            if(arg.ParameterType is CefStructArrayType && arg.ParameterType.IsIn) {
                cd.Lines = cd.Lines.Concat(new string[] { "Do not keep a reference to the elements of this array outside of this function." }).ToArray();
            }
            b.AppendSummary(cd);
            b.BeginBlock("public {0} {1}", arg.ParameterType.PublicSymbol, arg.PublicPropertyName);
            if(arg.ParameterType.IsIn) {
                b.BeginBlock("get");
                b.AppendLine("CheckAccess();");
                arg.EmitPublicEventArgGetterStatements(b);
                b.EndBlock();
            }
            if(arg.ParameterType.IsOut) {
                b.BeginBlock("set");
                b.AppendLine("CheckAccess();");
                arg.EmitPublicEventArgSetterStatements(b);
                b.EndBlock();
            }
            b.EndBlock();
        }

        if(!cb.Signature.PublicReturnType.IsVoid) {
            var cd = new CommentNode();
            cd.Lines = new string[] {
                string.Format("Set the return value for the <see cref=\"{0}.{1}\"/> callback.", ClassName, cb.PublicFunctionName),
                "Calling SetReturnValue() more then once per callback or from different event handlers will cause an exception to be thrown."
            };
            b.AppendSummary(cd);
            b.BeginBlock("public void SetReturnValue({0} returnValue)", cb.Signature.PublicReturnType.PublicSymbol);
            b.AppendLine("CheckAccess();");
            b.BeginIf("returnValueSet");
            b.AppendLine("throw new CfxException(\"The return value has already been set\");");
            b.EndBlock();
            b.AppendLine("returnValueSet = true;");
            b.AppendLine("this.m_returnValue = returnValue;");
            b.EndBlock();
        }

        if(cb.Signature.ManagedParameters.Count() > 1) {
            b.AppendLine();
            EmitEventToString(b, cb);
        }
        b.EndBlock();
    }

    private void EmitEventToString(CodeBuilder b, CefCallbackFunction cb) {
        b.BeginBlock("public override string ToString()");
        var format = new List<string>();
        var vars = new List<string>();
        var formatIndex = 0;
        for(var i = 1; i <= cb.Signature.ManagedParameters.Count() - 1; i++) {
            var arg = cb.Signature.ManagedParameters[i];
            if(arg.ParameterType.IsIn) {
                format.Add(string.Format("{0}={{{{{{{1}}}}}}}", arg.PublicPropertyName, formatIndex));
                vars.Add(arg.PublicPropertyName);
                formatIndex += 1;
            }
        }
        b.AppendLine("return String.Format(\"{0}\", {1});", string.Join(", ", format.ToArray()), string.Join(", ", vars.ToArray()));
        b.EndBlock();
    }

    public override void EmitRemoteCalls(CodeBuilder b, List<string> callIds) {

        b.AppendLine("using Event;");

        b.AppendLine();

        b.BeginRemoteCallClass(ClassName, callIds, "CtorWithGCHandleRemoteCall");
        b.AppendLine();
        b.BeginBlock("protected override void RemoteProcedure()");
        b.AppendLine("__retval = CfxApi.{0}.{1}_ctor(gcHandlePtr, 1);", ApiClassName, CfxName);
        b.EndBlock();
        b.EndBlock();
        b.AppendLine();

        if(NeedsWrapFunction) {
            b.BeginRemoteCallClass(ClassName, callIds, "GetGcHandleRemoteCall");
            b.AppendLine();
            b.BeginBlock("protected override void RemoteProcedure()");
            b.AppendLine("gc_handle = CfxApi.{0}.{1}_get_gc_handle(self);", ApiClassName, CfxName);
            b.EndBlock();
            b.EndBlock();
            b.AppendLine();
        }

        b.BeginRemoteCallClass(ClassName, callIds, "SetCallbackRemoteCall");
        b.AppendLine();
        b.BeginBlock("protected override void RemoteProcedure()");
        b.AppendLine("{0}RemoteClient.SetCallback(self, index, active);", ClassName);
        b.EndBlock();
        b.EndBlock();
        b.AppendLine();

        foreach(var cb in RemoteCallbackFunctions) {

            var sig = cb.Signature;

            b.BeginRemoteCallClass(cb.RemoteCallName, callIds, "RemoteEventCall");
            b.AppendLine();

            var inArgumentList = new List<string>();
            var outArgumentList = new List<string>();

            foreach(var arg in sig.Parameters) {
                if(!arg.IsThisArgument) {
                    foreach(var pm in arg.ParameterType.RemoteCallbackParameterList(arg.VarName)) {
                        if(pm.StartsWith("out ")) {
                            b.AppendLine("internal {0};", pm.Substring(4));
                            outArgumentList.Add(pm.Substring(pm.LastIndexOf(' ') + 1));
                        } else {
                            b.AppendLine("internal {0};", pm);
                            inArgumentList.Add(pm.Substring(pm.LastIndexOf(' ') + 1));
                        }
                    }
                }
            }
            b.AppendLine();

            if(!sig.ReturnType.IsVoid) {
                b.AppendLine("internal {0} __retval;", sig.ReturnType.PInvokeSymbol);
                b.AppendLine();
            }

            b.BeginBlock("protected override void WriteArgs(StreamHandler h)");
            b.AppendLine("h.Write(gcHandlePtr);");
            foreach(var pm in inArgumentList) {
                b.AppendLine("h.Write({0});", pm);
            }
            b.EndBlock();
            b.AppendLine();
            b.BeginBlock("protected override void ReadArgs(StreamHandler h)");
            b.AppendLine("h.Read(out gcHandlePtr);");
            foreach(var pm in inArgumentList) {
                b.AppendLine("h.Read(out {0});", pm);
            }
            b.EndBlock();
            b.AppendLine();
            b.BeginBlock("protected override void WriteReturn(StreamHandler h)");
            foreach(var pm in outArgumentList) {
                b.AppendLine("h.Write({0});", pm);
            }
            if(!sig.ReturnType.IsVoid) {
                b.AppendLine("h.Write(__retval);");
            }
            b.EndBlock();
            b.AppendLine();
            b.BeginBlock("protected override void ReadReturn(StreamHandler h)");
            foreach(var pm in outArgumentList) {
                b.AppendLine("h.Read(out {0});", pm);
            }
            if(!sig.ReturnType.IsVoid) {
                b.AppendLine("h.Read(out __retval);");
            }
            b.EndBlock();
            b.AppendLine();
            b.BeginBlock("protected override void RemoteProcedure()");

            b.AppendLine("var self = ({0})System.Runtime.InteropServices.GCHandle.FromIntPtr(gcHandlePtr).Target;", RemoteClassName);
            b.BeginIf("self == null || self.CallbacksDisabled");
            b.AppendLine("return;");
            b.EndBlock();
            if(cb.IsBasicEvent)
                b.AppendLine("var e = new CfrEventArgs();");
            else
                b.AppendLine("var e = new {0}(this);", cb.RemoteEventArgsClassName);
            b.AppendLine("e.connection = CfxRemoteCallContext.CurrentContext.connection;");
            b.AppendLine("self.m_{0}?.Invoke(self, e);", cb.PublicName);
            b.AppendLine("e.connection = null;");

            for(var i = 1; i <= sig.ManagedParameters.Count() - 1; i++) {
                sig.ManagedParameters[i].EmitPostRemoteRaiseEventStatements(b);
            }

            sig.EmitPostRemoteEventHandlerReturnValueStatements(b);

            b.EndBlock();
            b.EndBlock();
            b.AppendLine();
        }

    }

    public void EmitRemoteClient(CodeBuilder b) {
        b.BeginClass(ClassName + "RemoteClient", "internal static");
        b.AppendLine();

        b.BeginBlock("static {0}RemoteClient()", ClassName);

        foreach(var sm in RemoteCallbackFunctions) {
            b.AppendLine("{0}_native = {0};", sm.Name);
        }
        foreach(var sm in RemoteCallbackFunctions) {
            b.AppendLine("{0}_native_ptr = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate({0}_native);", sm.Name);
        }

        b.EndBlock();
        b.AppendLine();

        b.BeginBlock("internal static void SetCallback(IntPtr self, int index, bool active)");
        b.BeginBlock("switch(index)");
        foreach(var cb in RemoteCallbackFunctions) {
            b.AppendLine("case {0}:", cb.ClientCallbackIndex);
            b.IncreaseIndent();
            b.AppendLine("CfxApi.{0}.{1}_set_callback(self, index, active ? {2}_native_ptr : IntPtr.Zero);", ApiClassName, CfxName, cb.Name);
            b.AppendLine("break;");
            b.DecreaseIndent();
        }
        b.EndBlock();
        b.EndBlock();
        b.AppendLine();

        foreach(var cb in RemoteCallbackFunctions) {

            var sig = cb.Signature;

            b.AppendComment(cb.ToString());
            CodeSnippets.EmitPInvokeCallbackDelegate(b, cb.Name, cb.Signature);
            b.AppendLine("private static {0}_delegate {0}_native;", cb.Name);
            b.AppendLine("private static IntPtr {0}_native_ptr;", cb.Name);
            b.AppendLine();

            var inArgumentList = new List<string>();

            foreach(var arg in sig.Parameters) {
                if(!arg.IsThisArgument) {
                    foreach(var pm in arg.ParameterType.RemoteCallbackParameterList(arg.VarName)) {
                        if(!pm.StartsWith("out ")) {
                            inArgumentList.Add(pm.Substring(pm.LastIndexOf(' ') + 1));
                        }
                    }
                }
            }

            b.BeginFunction(cb.Name, "void", sig.PInvokeParameterList, "internal static");
            b.AppendLine("var call = new {0}RemoteEventCall();", cb.RemoteCallName);
            b.AppendLine("call.gcHandlePtr = gcHandlePtr;");
            foreach(var pm in inArgumentList) {
                b.AppendLine("call.{0} = {0};", pm);
            }
            b.AppendLine("call.RequestExecution();");
            foreach(var arg in sig.Parameters) {
                if(!arg.IsThisArgument)
                    arg.ParameterType.EmitPostRemoteCallbackStatements(b, arg.VarName);
            }
            if(!sig.ReturnType.IsVoid) {
                b.AppendLine("__retval = call.__retval;");
            }

            //sig.EmitPostPublicEventHandlerCallStatements(b);

            b.EndBlock();
            b.AppendLine();
        }


        b.EndBlock();
    }

    public override void EmitRemoteClass(CodeBuilder b) {

        b.AppendLine("using Event;");

        b.AppendLine();

        b.AppendSummaryAndRemarks(Comments, true, Category == StructCategory.Client);
        b.BeginClass(RemoteClassName + " : CfrBaseClient", GeneratorConfig.ClassModifiers(RemoteClassName));
        b.AppendLine();

        if(NeedsWrapFunction) {
            b.BeginFunction("Wrap", RemoteClassName, "RemotePtr remotePtr", "internal static");
            b.AppendLine("if(remotePtr == RemotePtr.Zero) return null;");
            b.AppendLine("var call = new {0}GetGcHandleRemoteCall();", ClassName);
            b.AppendLine("call.self = remotePtr.ptr;");
            b.AppendLine("call.RequestExecution(remotePtr.connection);");
            b.AppendLine("return ({0})System.Runtime.InteropServices.GCHandle.FromIntPtr(call.gc_handle).Target;", RemoteClassName);
            b.EndBlock();
            b.AppendLine();
            b.AppendLine();
        }

        b.AppendLine();

        b.AppendLine("private {0}(RemotePtr remotePtr) : base(remotePtr) {{}}", RemoteClassName);


        b.BeginBlock("public {0}() : base(new {1}CtorWithGCHandleRemoteCall())", RemoteClassName, ClassName);
        b.BeginBlock("lock(RemotePtr.connection.weakCache)");
        b.AppendLine("RemotePtr.connection.weakCache.Add(RemotePtr.ptr, this);");
        b.EndBlock();
        b.EndBlock();

        b.AppendLine();

        foreach(var cb in CallbackFunctions) {
            if(!GeneratorConfig.IsBrowserProcessOnly(CefStruct.Name + "::" + cb.Name)) {
                b.AppendSummaryAndRemarks(cb.Comments, true, true);
                b.BeginBlock("public event {0} {1}", cb.RemoteEventHandlerName, CSharp.Escape(cb.PublicName));
                b.BeginBlock("add");
                b.BeginBlock("if(m_{0} == null)", cb.PublicName);
                b.AppendLine("var call = new {0}SetCallbackRemoteCall();", ClassName);
                b.AppendLine("call.self = RemotePtr.ptr;");
                b.AppendLine("call.index = {0};", cb.ClientCallbackIndex);
                b.AppendLine("call.active = true;");
                b.AppendLine("call.RequestExecution(RemotePtr.connection);");
                b.EndBlock();
                b.AppendLine("m_{0} += value;", cb.PublicName);
                b.EndBlock();
                b.BeginBlock("remove");
                b.AppendLine("m_{0} -= value;", cb.PublicName);
                b.BeginBlock("if(m_{0} == null)", cb.PublicName);
                b.AppendLine("var call = new {0}SetCallbackRemoteCall();", ClassName);
                b.AppendLine("call.self = RemotePtr.ptr;");
                b.AppendLine("call.index = {0};", cb.ClientCallbackIndex);
                b.AppendLine("call.active = false;");
                b.AppendLine("call.RequestExecution(RemotePtr.connection);");
                b.EndBlock();
                b.EndBlock();
                b.EndBlock();
                b.AppendLine();
                b.AppendLine("internal {0} m_{1};", cb.RemoteEventHandlerName, cb.PublicName);
                b.AppendLine();
                b.AppendLine();
            }
        }

        b.EndBlock();

        b.AppendLine();
        b.BeginBlock("namespace Event");
        b.AppendLine();

        foreach(var cb in CallbackFunctions) {
            if(!GeneratorConfig.IsBrowserProcessOnly(CefStruct.Name + "::" + cb.Name)) {
                EmitRemoteEventArgsAndHandler(b, cb);
                b.AppendLine();
            }
        }

        b.EndBlock();
    }

    public void EmitRemoteEventArgsAndHandler(CodeBuilder b, CefCallbackFunction cb) {

        if(cb.IsBasicEvent)
            return;

        if(emittedEventHandlers.Contains(cb.RemoteEventHandlerName)) return;
        emittedEventHandlers.Add(cb.RemoteEventHandlerName);

        b.AppendSummaryAndRemarks(cb.Comments, true, true);
        b.AppendLine("public delegate void {0}(object sender, {1} e);", cb.RemoteEventHandlerName, cb.RemoteEventArgsClassName);
        b.AppendLine();

        b.AppendSummaryAndRemarks(cb.Comments, true, true);
        b.BeginClass(cb.RemoteEventArgsClassName + " : CfrEventArgs", GeneratorConfig.ClassModifiers(cb.RemoteEventArgsClassName));
        b.AppendLine();

        b.AppendLine("private {0}RemoteEventCall call;", cb.RemoteCallName);
        b.AppendLine();

        for(var i = 1; i <= cb.Signature.ManagedParameters.Count() - 1; i++) {
            cb.Signature.ManagedParameters[i].EmitRemoteEventArgFields(b);
        }
        b.AppendLine();

        if(!cb.Signature.PublicReturnType.IsVoid) {
            b.AppendLine("internal {0} m_returnValue;", cb.Signature.PublicReturnType.RemoteSymbol);
            b.AppendLine("private bool returnValueSet;");
            b.AppendLine();
        }

        b.AppendLine("internal {0}({1}RemoteEventCall call) {{ this.call = call; }}", cb.RemoteEventArgsClassName, cb.RemoteCallName);
        b.AppendLine();

        for(var i = 1; i <= cb.Signature.ManagedParameters.Count() - 1; i++) {
            var arg = cb.Signature.ManagedParameters[i];
            var cd = new CommentNode();
            if(arg.ParameterType.IsIn && arg.ParameterType.IsOut) {
                cd.Lines = new string[] { string.Format("Get or set the {0} parameter for the <see cref=\"{1}.{2}\"/> render process callback.", arg.PublicPropertyName, CefStruct.RemoteSymbol, cb.PublicFunctionName) };
            } else if(arg.ParameterType.IsIn) {
                cd.Lines = new string[] { string.Format("Get the {0} parameter for the <see cref=\"{1}.{2}\"/> render process callback.", arg.PublicPropertyName, CefStruct.RemoteSymbol, cb.PublicFunctionName) };
            } else {
                cd.Lines = new string[] { string.Format("Set the {0} out parameter for the <see cref=\"{1}.{2}\"/> render process callback.", arg.PublicPropertyName, CefStruct.RemoteSymbol, cb.PublicFunctionName) };
            }
            if(arg.ParameterType is CefStructArrayType && arg.ParameterType.IsIn) {
                cd.Lines = cd.Lines.Concat(new string[] { "Do not keep a reference to the elements of this array outside of this function." }).ToArray();
            }
            b.AppendSummary(cd);
            b.BeginBlock("public {0} {1}", arg.ParameterType.RemoteSymbol, arg.PublicPropertyName);
            if(arg.ParameterType.IsIn) {
                b.BeginBlock("get");
                b.AppendLine("CheckAccess();");
                arg.EmitRemoteEventArgGetterStatements(b);
                b.EndBlock();
            }
            if(arg.ParameterType.IsOut) {
                b.BeginBlock("set");
                b.AppendLine("CheckAccess();");
                arg.EmitRemoteEventArgSetterStatements(b);
                b.EndBlock();
            }
            b.EndBlock();
        }
        if(!cb.Signature.PublicReturnType.IsVoid) {
            var cd = new CommentNode();
            cd.Lines = new string[] {
                string.Format("Set the return value for the <see cref=\"{0}.{1}\"/> render process callback.", CefStruct.RemoteClassName, cb.PublicFunctionName),
                "Calling SetReturnValue() more then once per callback or from different event handlers will cause an exception to be thrown."
            };
            b.AppendSummary(cd);
            b.BeginBlock("public void SetReturnValue({0} returnValue)", cb.Signature.PublicReturnType.RemoteSymbol);
            b.BeginIf("returnValueSet");
            b.AppendLine("throw new CfxException(\"The return value has already been set\");");
            b.EndBlock();
            b.AppendLine("m_returnValue = returnValue;");
            b.AppendLine("returnValueSet = true;");
            b.EndBlock();
        }

        if(cb.Signature.ManagedParameters.Count() > 1) {
            b.AppendLine();
            EmitEventToString(b, cb);
        }
        b.EndBlock();
    }
}
