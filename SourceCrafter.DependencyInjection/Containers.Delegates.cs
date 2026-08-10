using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Text;

delegate void AppendValue(
    StringBuilder code,
    bool asyncContext = false,
    bool interceptorContext = false);

delegate void CommaSeparateBuilder(
    ref bool useIComma,
    int deepParamCount,
    string baseIndent);

delegate bool ChildDependencyHandler(
    bool childExists,
    bool isChildValid,
    Lifetime childLifetime,
    AsyncType isChildAsync,
    int childParamCount,
    AppendValue AppendParam,
    bool isNullChildType,
    bool isUnkeyedInternalPrimitive);

delegate void DisposeBuilder(
    StringBuilder code,
    bool await = true);

delegate void AppendInterceptor(
    StringBuilder code,
    ref int i);

