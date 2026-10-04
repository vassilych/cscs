using SplitAndMerge;
var interpreter = new Interpreter();
interpreter.InitStandalone();
new CSCSMath.CscsMathModule().CreateInstance(interpreter);
RoslynCompiler.CacheEnabled = false;
interpreter.ProcessFile(Path.GetFullPath(args[0]), true);
