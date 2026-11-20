#include <windows.h>
#include <shobjidl.h>
#include <cstdio>

using GetClassObject = HRESULT (STDAPICALLTYPE*)(REFCLSID, REFIID, void**);
using CanUnload = HRESULT (STDAPICALLTYPE*)();
static const CLSID CommandClsid = {0x7eab048a,0x3e61,0x47ac,{0x91,0xa9,0x46,0x9f,0x93,0xa2,0xb8,0xc4}};

int wmain(int argc, wchar_t** argv) {
    if (argc != 2) return 1;
    auto module = LoadLibraryW(argv[1]);
    if (!module) return 2;
    auto getClass = reinterpret_cast<GetClassObject>(GetProcAddress(module, "DllGetClassObject"));
    auto canUnload = reinterpret_cast<CanUnload>(GetProcAddress(module, "DllCanUnloadNow"));
    if (!getClass || !canUnload || canUnload() != S_OK) return 3;
    IClassFactory* factory = nullptr;
    if (FAILED(getClass(CommandClsid, IID_PPV_ARGS(&factory))) || !factory || canUnload() != S_FALSE) return 4;
    IExplorerCommand* command = nullptr;
    if (FAILED(factory->CreateInstance(nullptr, IID_PPV_ARGS(&command))) || !command) return 5;
    PWSTR title = nullptr;
    if (FAILED(command->GetTitle(nullptr, &title)) || !title || wcscmp(title, L"Open in Akki Image Tools") != 0) return 6;
    CoTaskMemFree(title);
    if (command->Invoke(nullptr, nullptr) != E_INVALIDARG) return 7;
    GUID guid{};
    if (FAILED(command->GetCanonicalName(&guid)) || guid != CommandClsid) return 8;
    command->Release(); factory->Release();
    if (canUnload() != S_OK) return 9;
    FreeLibrary(module);
    std::puts("PASS: Explorer command COM creation, title, invalid invocation, identity and lifetime");
    return 0;
}
