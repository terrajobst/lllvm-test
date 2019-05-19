using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using LLVMSharp;

internal sealed class Program
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int Add(int a, int b);

    private static void Main(string[] args)
    {
        var appPath = Path.GetDirectoryName(Environment.GetCommandLineArgs()[0]);

        LLVMBool Success = new LLVMBool(0);
        LLVMModuleRef mod = LLVM.ModuleCreateWithName("LLVMSharpIntro");

        LLVMTypeRef[] param_types = { LLVM.Int32Type(), LLVM.Int32Type() };
        LLVMTypeRef ret_type = LLVM.FunctionType(LLVM.Int32Type(), param_types, false);
        LLVMValueRef sum = LLVM.AddFunction(mod, "sum", ret_type);

        LLVMBasicBlockRef entry = LLVM.AppendBasicBlock(sum, "entry");

        LLVMBuilderRef builder = LLVM.CreateBuilder();
        LLVM.PositionBuilderAtEnd(builder, entry);
        LLVMValueRef tmp = LLVM.BuildAdd(builder, LLVM.GetParam(sum, 0), LLVM.GetParam(sum, 1), "tmp");
        LLVM.BuildRet(builder, tmp);

        if (LLVM.VerifyModule(mod, LLVMVerifierFailureAction.LLVMPrintMessageAction, out var error) != Success)
        {
            Console.WriteLine($"Error: {error}");
        }

        LLVM.LinkInMCJIT();

        LLVM.InitializeX86TargetMC();
        LLVM.InitializeX86Target();
        LLVM.InitializeX86TargetInfo();
        LLVM.InitializeX86AsmParser();
        LLVM.InitializeX86AsmPrinter();

        LLVMMCJITCompilerOptions options = new LLVMMCJITCompilerOptions { NoFramePointerElim = 1 };
        LLVM.InitializeMCJITCompilerOptions(options);
        if (LLVM.CreateMCJITCompilerForModule(out var engine, mod, options, out error) != Success)
        {
            Console.WriteLine($"Error: {error}");
        }

        var addMethod = (Add)Marshal.GetDelegateForFunctionPointer(LLVM.GetPointerToGlobal(engine, sum), typeof(Add));
        int result = addMethod(10, 10);

        Console.WriteLine("Result of sum is: " + result);

        if (LLVM.WriteBitcodeToFile(mod, Path.Join(appPath, "sum.bc")) != 0)
            Console.WriteLine("error writing bitcode to file, skipping");

        LLVM.DumpModule(mod);

        //------------------------------------------------------------

        var cpu = "x86-64";
        var features = "";
        var sumObjPath = Path.Join(appPath, "sum.obj");
        var targetTriple = Marshal.PtrToStringAnsi(LLVM.GetDefaultTargetTriple());
        // LLVM.InitializeNativeTarget();
        Console.WriteLine(targetTriple);

        var firstTarget = LLVM.GetFirstTarget();
        var optimizationLevel = LLVMCodeGenOptLevel.LLVMCodeGenLevelDefault;
        var relocationMode = LLVMRelocMode.LLVMRelocDefault;
        var codeModel = LLVMCodeModel.LLVMCodeModelDefault;
        var targetMachine = LLVM.CreateTargetMachine(firstTarget, targetTriple, cpu, features, optimizationLevel, relocationMode, codeModel);
        var fileType = LLVMCodeGenFileType.LLVMObjectFile;

        if (LLVM.TargetMachineEmitToFile(targetMachine, mod, Marshal.StringToHGlobalAnsi(sumObjPath), fileType, out error))
            Console.WriteLine("ERROR: " + error);

        // Compile & link

        var mainCppPath = Path.Join(appPath, "main.cpp");
        var mainObjPath = Path.Join(appPath, "main.obj");
        var mainExePath = Path.Join(appPath, "main.exe");

        var mainCppContents = @"
            #include <stdio.h>

            extern ""C"" int sum(int a, int b);

            void main()
            {
                int x = sum(48, -6);
                printf(""Hello world: %d\n"", x);
            }
        ";

        File.WriteAllText(mainCppPath, mainCppContents);

        var clPath = @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Preview\VC\Tools\MSVC\14.21.27619\bin\Hostx64\x64\cl.exe";
        var linkPath = @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Preview\VC\Tools\MSVC\14.21.27619\bin\Hostx64\x64\link.exe";

        var includePath = @"C:\Program Files (x86)\Windows Kits\10\include\10.0.17763.0\ucrt;C:\Program Files (x86)\Microsoft Visual Studio\2019\Preview\VC\Tools\MSVC\14.21.27619\include";
        Environment.SetEnvironmentVariable("INCLUDE", includePath);

        var libPath = @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Preview\VC\Tools\MSVC\14.21.27619\lib\x64;c:\Program Files (x86)\Windows Kits\10\Lib\10.0.17763.0\um\x64\;c:\Program Files (x86)\Windows Kits\10\Lib\10.0.17763.0\ucrt\x64\";
        Environment.SetEnvironmentVariable("LIB", libPath);

        var clArgs = $"\"{mainCppPath}\" /Fo:\"{mainObjPath}\" /c /nologo";
        Console.WriteLine(clPath + " " + clArgs);
        Process.Start(clPath, clArgs).WaitForExit();

        var linkArgs = $"\"{mainObjPath}\" \"{sumObjPath}\" /out:\"{mainExePath}\" /nologo";
        Console.WriteLine(linkPath + " " + linkArgs);
        Process.Start(linkPath, linkArgs).WaitForExit();

        //------------------------------------------------------------

        LLVM.DisposeBuilder(builder);
        LLVM.DisposeExecutionEngine(engine);
    }
}