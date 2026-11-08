import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import App from '../App';

describe('School Admission System UI', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
  });

  it('renders header, student id input, and navigation tabs', () => {
    render(<App />);
    expect(screen.getByText(/অনলাইন স্কুল ভর্তি সিস্টেম/i)).toBeInTheDocument();
    expect(screen.getByPlaceholderText(/১৭ অঙ্কের জন্ম নিবন্ধন নম্বর/i)).toBeInTheDocument();
    expect(screen.getByText(/ক্লাস ও আসন তালিকা/i)).toBeInTheDocument();
    expect(screen.getByText(/আমার আবেদন/i)).toBeInTheDocument();
    expect(screen.getByText(/ক্লাস তৈরি \(School Admin\)/i)).toBeInTheDocument();
  });

  it('allows changing student id and persists to localStorage', () => {
    render(<App />);
    const input = screen.getByPlaceholderText(/১৭ অঙ্কের জন্ম নিবন্ধন নম্বর/i) as HTMLInputElement;

    fireEvent.change(input, { target: { value: '20152691234567899' } });
    expect(input.value).toBe('20152691234567899');
    expect(localStorage.getItem('school_student_id')).toBe('20152691234567899');
  });

  it('switches between tabs accurately', () => {
    render(<App />);

    const adminTabBtn = screen.getByText(/ক্লাস তৈরি \(School Admin\)/i);
    fireEvent.click(adminTabBtn);

    expect(screen.getByPlaceholderText(/যেমন: Class 6 - Morning Shift/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /ক্লাস তৈরি করুন/i })).toBeInTheDocument();
  });
});

