import { describe, expect, it } from 'vitest';
import { pdfText } from './pdf-text';

describe('pdfText', () => {
  it('spells out the peso sign the PDF fonts cannot draw', () => {
    expect(pdfText('Net ₱4,991.39')).toBe('Net PHP 4,991.39');
    expect(pdfText('-₱5.00')).toBe('-PHP 5.00');
    expect(pdfText('No amount')).toBe('No amount');
  });
});
