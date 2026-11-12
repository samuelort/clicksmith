import SwiftUI

/// Drop-in replacement for `@State`.
///
/// In recent SDKs `@State` is a Swift macro whose plugin ships with Xcode but not with the
/// Command Line Tools ("plugin for module 'SwiftUIMacros' not found"). `@Local` is a plain
/// property wrapper built on `@StateObject`, so it compiles with either toolchain:
/// the value is owned by the view, survives re-renders, and `$x` gives a Binding.
/// The initial value is an autoclosure, evaluated once — like `@State`.
@propertyWrapper
struct Local<Value>: DynamicProperty {
    final class Storage: ObservableObject {
        @Published var value: Value
        init(_ value: Value) { self.value = value }
    }

    @StateObject private var storage: Storage

    init(wrappedValue: @autoclosure @escaping () -> Value) {
        _storage = StateObject(wrappedValue: Storage(wrappedValue()))
    }

    var wrappedValue: Value {
        get { storage.value }
        nonmutating set { storage.value = newValue }
    }

    var projectedValue: Binding<Value> { $storage.value }
}
