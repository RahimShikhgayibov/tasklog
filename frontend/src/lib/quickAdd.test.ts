import { describe, expect, it } from "vitest";
import type { Category } from "../api/types";
import { parseQuickAdd } from "./quickAdd";

const categories: Category[] = [
  { id: "c-is", name: "İş", color: "#3451D1" },
  { id: "c-tehsil", name: "Təhsil", color: "#C2410C" },
];

// Wednesday 4 Nov 2026, 13:00 local
const now = new Date(2026, 10, 4, 13, 0);

describe("parseQuickAdd", () => {
  it("leaves plain text alone", () => {
    const r = parseQuickAdd("Hesabat hazırla", categories, now);
    expect(r).toEqual({ title: "Hesabat hazırla", tags: [] });
  });

  it("parses everything from the placeholder example", () => {
    const r = parseQuickAdd("Hesabat hazırla sabah 15:00 #iş !yüksək", categories, now);
    expect(r.title).toBe("Hesabat hazırla");
    expect(r.tags).toEqual(["iş"]);
    expect(r.priority).toBe("High");
    expect(r.dueAt).toEqual(new Date(2026, 10, 5, 15, 0));
  });

  it("matches a category by name, case-insensitively with Azerbaijani İ", () => {
    const r = parseQuickAdd("Code review @iş", categories, now);
    expect(r.categoryId).toBe("c-is");
    expect(r.title).toBe("Code review");
  });

  it("keeps an unknown @category in the title", () => {
    const r = parseQuickAdd("Zəng et @ailə", categories, now);
    expect(r.categoryId).toBeUndefined();
    expect(r.title).toBe("Zəng et @ailə");
  });

  it("a day without a time means end of that day", () => {
    const r = parseQuickAdd("Kitab oxu bu gün", categories, now);
    expect(r.dueAt).toEqual(new Date(2026, 10, 4, 23, 59));
    expect(r.title).toBe("Kitab oxu");
  });

  it("a time without a day means today", () => {
    const r = parseQuickAdd("Zəng 18:30", categories, now);
    expect(r.dueAt).toEqual(new Date(2026, 10, 4, 18, 30));
  });

  it("parses dd.mm and dd.mm.yyyy dates", () => {
    expect(parseQuickAdd("İmtahan 20.11", categories, now).dueAt).toEqual(new Date(2026, 10, 20, 23, 59));
    expect(parseQuickAdd("Viza 05.01.2027 10:00", categories, now).dueAt).toEqual(new Date(2027, 0, 5, 10, 0));
  });

  it("ignores impossible dates", () => {
    const r = parseQuickAdd("Test 31.02", categories, now);
    expect(r.dueAt).toBeUndefined();
  });

  it("parses recurrence without leaving 'gün' behind", () => {
    const r = parseQuickAdd("İngilis dili — 20 söz hər gün 09:00 #english", categories, now);
    expect(r.recurrence).toBe("Daily");
    expect(r.title).toBe("İngilis dili — 20 söz");
    expect(r.tags).toEqual(["english"]);
    expect(r.dueAt).toEqual(new Date(2026, 10, 4, 9, 0));
  });

  it("supports short priority forms and several tags", () => {
    const r = parseQuickAdd("Deploy !1 #devops #backend", categories, now);
    expect(r.priority).toBe("High");
    expect(r.tags).toEqual(["devops", "backend"]);
    expect(r.title).toBe("Deploy");
  });

  it("does not treat words that merely contain 'sabah' as a day", () => {
    const r = parseQuickAdd("Sabahlıq idman", categories, now);
    expect(r.dueAt).toBeUndefined();
    expect(r.title).toBe("Sabahlıq idman");
  });
});
