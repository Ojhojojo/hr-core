import { describe, expect, it } from 'vitest';
import {
  BIR_2316_THIRTEENTH_NON_TAXABLE_CAP,
  computeBir2316,
  emptyBir2316Inputs,
  missingLabels,
  monthNames,
  sanitizeMoney,
} from './bir-2316';

const payroll = { gross: 360000, sss: 18000, philHealth: 9000, pagIbig: 2400, wht: 25000 };

describe('BIR 2316 computation', () => {
  it('keeps the 13th-month cap at 90,000 and treats only the excess as taxable', () => {
    expect(BIR_2316_THIRTEENTH_NON_TAXABLE_CAP).toBe(90000);

    const at = computeBir2316(payroll, {
      ...emptyBir2316Inputs(),
      thirteenthAndOtherBenefits: 90000,
    });
    expect(at.thirteenthNonTaxable).toBe(90000);
    expect(at.thirteenthExcessTaxable).toBe(0);

    const over = computeBir2316(payroll, {
      ...emptyBir2316Inputs(),
      thirteenthAndOtherBenefits: 100000,
    });
    expect(over.thirteenthNonTaxable).toBe(90000);
    expect(over.thirteenthExcessTaxable).toBe(10000);
    expect(over.thirteenthAndOtherBenefits).toBe(100000);
  });

  it('adds HR-entered benefits to gross and takes non-taxable items out again', () => {
    const result = computeBir2316(payroll, {
      thirteenthAndOtherBenefits: 30000,
      deMinimis: 6000,
      otherNonTaxable: 1500,
      previousTaxableCompensation: 0,
      previousTaxWithheld: 0,
    });

    // Gross = payroll gross + 13th month + de minimis + other non-taxable.
    expect(result.grossCompensation).toBe(360000 + 30000 + 6000 + 1500);
    // Contributions are employee shares: SSS + PhilHealth + Pag-IBIG.
    expect(result.contributions).toBe(18000 + 9000 + 2400);
    expect(result.totalNonTaxable).toBe(30000 + 6000 + 29400 + 1500);
    expect(result.taxableCompensationPresent).toBe(360000 - 29400);
  });

  it('makes taxable compensation equal payroll gross less contributions plus the 13th excess', () => {
    const result = computeBir2316(payroll, {
      thirteenthAndOtherBenefits: 120000,
      deMinimis: 5000,
      otherNonTaxable: 0,
      previousTaxableCompensation: 0,
      previousTaxWithheld: 0,
    });
    expect(result.taxableCompensationPresent).toBe(360000 - 29400 + 30000);
  });

  it('adds previous-employer figures and totals the taxes withheld', () => {
    const result = computeBir2316(payroll, {
      thirteenthAndOtherBenefits: 0,
      deMinimis: 0,
      otherNonTaxable: 0,
      previousTaxableCompensation: 80000,
      previousTaxWithheld: 4000,
    });
    expect(result.totalTaxableCompensation).toBe(360000 - 29400 + 80000);
    expect(result.taxWithheldPresent).toBe(25000);
    expect(result.previousTaxWithheld).toBe(4000);
    expect(result.totalTaxWithheld).toBe(29000);
  });

  it('treats blank, negative and non-finite inputs as zero', () => {
    expect(sanitizeMoney(-5)).toBe(0);
    expect(sanitizeMoney(Number.NaN)).toBe(0);
    expect(sanitizeMoney(null)).toBe(0);
    expect(sanitizeMoney(12.5)).toBe(12.5);

    const result = computeBir2316(payroll, {
      thirteenthAndOtherBenefits: -100,
      deMinimis: Number.NaN,
      otherNonTaxable: Number.NaN,
      previousTaxableCompensation: -1,
      previousTaxWithheld: -1,
    });
    expect(result.thirteenthAndOtherBenefits).toBe(0);
    expect(result.deMinimis).toBe(0);
    expect(result.grossCompensation).toBe(360000);
    expect(result.totalTaxWithheld).toBe(25000);
  });
});

describe('BIR 2316 labels', () => {
  it('names the months with no locked run', () => {
    expect(monthNames([2, 12])).toEqual(['February', 'December']);
  });

  it('turns missing keys into the labels HR sees', () => {
    expect(missingLabels(['employee_tin', 'employer_address'])).toEqual([
      "Employee's TIN",
      'Shop address',
    ]);
  });
});
