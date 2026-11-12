import Foundation
import ClicksmithCore

// Runs the core checks without XCTest (works with Command Line Tools only).
let results = CoreChecks.all()
let failed = results.filter { !$0.1 }
for (name, _) in failed { print("  FAIL: \(name)") }
print("Self-test: \(results.count - failed.count)/\(results.count) passed")
exit(failed.isEmpty ? 0 : 1)
