#include <windows.h>
#include <shobjidl.h>
#include <shlwapi.h>
#include <string>
#include <atomic>
#include <new>

// {7EAB048A-3E61-47AC-91A9-469F93A2B8C4}
static const CLSID CommandClsid = {0x7eab048a,0x3e61,0x47ac,{0x91,0xa9,0x46,0x9f,0x93,0xa2,0xb8,0xc4}};
static HMODULE moduleHandle;
static std::atomic<long> objectCount{0};

static std::wstring ModuleDirectory() {
    std::wstring path(32768, L'\0');
    DWORD length = GetModuleFileNameW(moduleHandle, path.data(), static_cast<DWORD>(path.size()));
    if (!length || length >= path.size()) return {};
    path.resize(length);
    auto slash = path.find_last_of(L"\\/");
    return slash == std::wstring::npos ? std::wstring{} : path.substr(0, slash + 1);
}

// Windows filenames cannot contain quotes. Doubling trailing backslashes keeps
// them literal when the Windows command line parser sees the closing quote.
static std::wstring Quote(const std::wstring& value) {
    std::wstring result = L"\"";
    size_t slashes = 0;
    for (wchar_t c : value) {
        if (c == L'\\') { ++slashes; continue; }
        if (c == L'"') result.append(slashes * 2 + 1, L'\\');
        else result.append(slashes, L'\\');
        slashes = 0; result += c;
    }
    result.append(slashes * 2, L'\\');
    return result + L"\"";
}

class ExplorerCommand final : public IExplorerCommand {
    std::atomic<ULONG> refs{1};
public:
    ExplorerCommand() { ++objectCount; }
    ~ExplorerCommand() { --objectCount; }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id, void** out) override {
        if (!out) return E_POINTER;
        *out = nullptr;
        if (id == IID_IUnknown || id == IID_IExplorerCommand) { *out = static_cast<IExplorerCommand*>(this); AddRef(); return S_OK; }
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++refs; }
    ULONG STDMETHODCALLTYPE Release() override { auto count = --refs; if (!count) delete this; return count; }
    HRESULT STDMETHODCALLTYPE GetTitle(IShellItemArray*, PWSTR* title) override { return SHStrDupW(L"Open in Akki Image Tools", title); }
    HRESULT STDMETHODCALLTYPE GetIcon(IShellItemArray*, PWSTR* icon) override {
        try { auto path = ModuleDirectory() + L"Akki.ImageConverter.exe"; return SHStrDupW(path.c_str(), icon); }
        catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
        catch (...) { return E_FAIL; }
    }
    HRESULT STDMETHODCALLTYPE GetToolTip(IShellItemArray*, PWSTR* tip) override { return SHStrDupW(L"Add selected images to Akki Image Tools", tip); }
    HRESULT STDMETHODCALLTYPE GetCanonicalName(GUID* name) override { if (!name) return E_POINTER; *name = CommandClsid; return S_OK; }
    HRESULT STDMETHODCALLTYPE GetState(IShellItemArray*, BOOL, EXPCMDSTATE* state) override { if (!state) return E_POINTER; *state = ECS_ENABLED; return S_OK; }
    HRESULT STDMETHODCALLTYPE Invoke(IShellItemArray* items, IBindCtx*) override {
        try {
        if (!items) return E_INVALIDARG;
        auto exe = ModuleDirectory() + L"Akki.ImageConverter.exe";
        if (GetFileAttributesW(exe.c_str()) == INVALID_FILE_ATTRIBUTES) return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
        DWORD count = 0; HRESULT hr = items->GetCount(&count); if (FAILED(hr)) return hr;
        std::wstring command = Quote(exe);
        for (DWORD i = 0; i < count; ++i) {
            IShellItem* item = nullptr;
            if (FAILED(items->GetItemAt(i, &item))) continue;
            PWSTR name = nullptr;
            hr = item->GetDisplayName(SIGDN_FILESYSPATH, &name);
            item->Release();
            if (FAILED(hr) || !name) continue;
            auto part = L" " + Quote(name); CoTaskMemFree(name);
            if (command.size() + part.size() >= 32767) return HRESULT_FROM_WIN32(ERROR_BUFFER_OVERFLOW);
            command += part;
        }
        if (command == Quote(exe)) return E_INVALIDARG;
        STARTUPINFOW startup{}; startup.cb = sizeof(startup); PROCESS_INFORMATION process{};
        if (!CreateProcessW(exe.c_str(), command.data(), nullptr, nullptr, FALSE, 0, nullptr, nullptr, &startup, &process)) return HRESULT_FROM_WIN32(GetLastError());
        CloseHandle(process.hThread); CloseHandle(process.hProcess); return S_OK;
        } catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
        catch (...) { return E_FAIL; }
    }
    HRESULT STDMETHODCALLTYPE GetFlags(EXPCMDFLAGS* flags) override { if (!flags) return E_POINTER; *flags = ECF_DEFAULT; return S_OK; }
    HRESULT STDMETHODCALLTYPE EnumSubCommands(IEnumExplorerCommand** commands) override { if (commands) *commands = nullptr; return E_NOTIMPL; }
};

class Factory final : public IClassFactory {
    std::atomic<ULONG> refs{1};
public:
    Factory() { ++objectCount; }
    ~Factory() { --objectCount; }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id, void** out) override {
        if (!out) return E_POINTER; *out = nullptr;
        if (id == IID_IUnknown || id == IID_IClassFactory) { *out = static_cast<IClassFactory*>(this); AddRef(); return S_OK; }
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++refs; }
    ULONG STDMETHODCALLTYPE Release() override { auto count = --refs; if (!count) delete this; return count; }
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID id, void** out) override {
        if (!out) return E_POINTER; *out = nullptr; if (outer) return CLASS_E_NOAGGREGATION;
        auto instance = new(std::nothrow) ExplorerCommand(); if (!instance) return E_OUTOFMEMORY;
        auto hr = instance->QueryInterface(id, out); instance->Release(); return hr;
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) override { if (lock) ++objectCount; else --objectCount; return S_OK; }
};

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) { if (reason == DLL_PROCESS_ATTACH) { moduleHandle = instance; DisableThreadLibraryCalls(instance); } return TRUE; }
STDAPI DllCanUnloadNow() { return objectCount == 0 ? S_OK : S_FALSE; }
STDAPI DllGetClassObject(REFCLSID clsid, REFIID id, void** out) {
    if (!out) return E_POINTER; *out = nullptr; if (clsid != CommandClsid) return CLASS_E_CLASSNOTAVAILABLE;
    auto factory = new(std::nothrow) Factory(); if (!factory) return E_OUTOFMEMORY;
    auto hr = factory->QueryInterface(id, out); factory->Release(); return hr;
}
