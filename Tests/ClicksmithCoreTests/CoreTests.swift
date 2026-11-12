import XCTest
@testable import ClicksmithCore

final class CoreTests: XCTestCase {
    func testAllCoreChecks() {
        for (name, passed) in CoreChecks.all() {
            XCTAssertTrue(passed, name)
        }
    }

    func testCheckCountIsStable() {
        XCTAssertGreaterThanOrEqual(CoreChecks.all().count, 78)
    }
}
