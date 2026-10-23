import { describe, expect, it } from 'vitest';
import { Announcement, announcementStatus, isAnnouncementVisible } from './announcement-visibility';

describe('isAnnouncementVisible', () => {
  const today = '2026-10-03';

  it('hides an announcement whose end date was yesterday', () => {
    expect(
      isAnnouncementVisible(
        { active: true, startDate: '2026-09-01', endDate: '2026-10-02' },
        today,
      ),
    ).toBe(false);
  });

  it('shows an active announcement that includes today', () => {
    expect(
      isAnnouncementVisible(
        { active: true, startDate: '2026-10-01', endDate: '2026-10-03' },
        today,
      ),
    ).toBe(true);
  });
});

describe('announcementStatus', () => {
  const today = '2026-10-16';

  it('returns Active when active is true and today falls within start and end dates', () => {
    const status = announcementStatus(
      { active: true, startDate: '2026-10-10', endDate: '2026-10-20' },
      today,
    );
    expect(status).toEqual({
      status: 'active',
      label: 'Active',
      tone: 'ok',
    });
  });

  it('returns Active on the start date and end date boundary', () => {
    expect(
      announcementStatus({ active: true, startDate: '2026-10-16', endDate: '2026-10-16' }, today),
    ).toEqual({
      status: 'active',
      label: 'Active',
      tone: 'ok',
    });
  });

  it('returns Scheduled when active is true and start date is in the future', () => {
    const status = announcementStatus(
      { active: true, startDate: '2026-10-20', endDate: '2026-10-31' },
      today,
    );
    expect(status).toEqual({
      status: 'scheduled',
      label: 'Scheduled',
      tone: 'info',
    });
  });

  it('returns Expired when active is true and end date has passed (e.g. Bonus notice)', () => {
    const bonusAnnouncement: Announcement = {
      id: 'ann-bonus',
      title: 'Bonus',
      body: 'Quarterly bonus distribution details',
      startDate: '2026-10-11',
      endDate: '2026-10-13',
      active: true,
    };
    const status = announcementStatus(bonusAnnouncement, today);
    expect(status).toEqual({
      status: 'expired',
      label: 'Expired',
      tone: 'neutral',
    });
  });

  it('returns Inactive when active is false even if dates enclose today', () => {
    const status = announcementStatus(
      { active: false, startDate: '2026-10-01', endDate: '2026-10-31' },
      today,
    );
    expect(status).toEqual({
      status: 'inactive',
      label: 'Inactive',
      tone: 'neutral',
    });
  });

  it('returns Inactive when active is false for future or past notices', () => {
    expect(
      announcementStatus({ active: false, startDate: '2026-10-20', endDate: '2026-10-30' }, today),
    ).toEqual({
      status: 'inactive',
      label: 'Inactive',
      tone: 'neutral',
    });
    expect(
      announcementStatus({ active: false, startDate: '2026-10-01', endDate: '2026-10-05' }, today),
    ).toEqual({
      status: 'inactive',
      label: 'Inactive',
      tone: 'neutral',
    });
  });
});
