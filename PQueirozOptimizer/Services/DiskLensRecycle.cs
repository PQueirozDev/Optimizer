using System.Runtime.InteropServices;

namespace PQueirozOptimizer.Services;

internal static class DiskLensRecycle
{
    // IFileOperation exige STA. RECYCLEONDELETE solicita explicitamente a Lixeira (Windows 8+).
    internal static void File(string path)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            object? operation = null; IShellItem? item = null;
            try
            {
                var iid = typeof(IShellItem).GUID;
                Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out item));
                operation = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"), true)!);
                var files = (IFileOperation)operation!;
                files.SetOperationFlags(0x00080000 | 0x00100000 | 0x0400 | 0x0004 | 0x0010 | 0x20000000 | 0x2000);
                files.DeleteItem(item, IntPtr.Zero);
                files.PerformOperations();
                files.GetAnyOperationsAborted(out bool aborted);
                if (aborted || System.IO.File.Exists(path)) throw new System.IO.IOException("O Windows não enviou o arquivo à Lixeira.");
                completed.SetResult();
            }
            catch (Exception ex) { completed.SetException(ex); }
            finally
            {
                if (item is not null) Marshal.ReleaseComObject(item);
                if (operation is not null) Marshal.ReleaseComObject(operation);
            }
        }) { IsBackground = true, Name = "DiskLens Lixeira" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        completed.Task.GetAwaiter().GetResult();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid, out IShellItem item);

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem { }

    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise(IntPtr sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog(IntPtr dialog);
        void SetProperties(IntPtr properties);
        void SetOwnerWindow(IntPtr owner);
        void ApplyPropertiesToItem(IShellItem item);
        void ApplyPropertiesToItems(IntPtr items);
        void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
        void RenameItems(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string name);
        void MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, IntPtr sink);
        void MoveItems(IntPtr items, IShellItem destination);
        void CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, IntPtr sink);
        void CopyItems(IntPtr items, IShellItem destination);
        void DeleteItem(IShellItem item, IntPtr sink);
        void DeleteItems(IntPtr items);
        void NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string? template, IntPtr sink);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
}
