---
layout: doc
title: Brand colors
nav_order: 9
nav_exclude: false
permalink: /brand-colors/
description: Packata brand color palette and usage guidance.
---

<style>
.packata-color-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(230px, 1fr));
  gap: 1.25rem;
  margin: 1.5rem 0 2rem 0;
}

.packata-color-card {
  border: 1px solid #D5E3DF;
  border-radius: 12px;
  overflow: hidden;
  background: #ffffff;
  box-shadow: 0 6px 18px rgba(0,0,0,0.06);
}

.packata-color-swatch {
  height: 110px;
  display: flex;
  align-items: end;
  justify-content: start;
  padding: 0.85rem 1rem;
  font-weight: 700;
  letter-spacing: 0.01em;
}

.packata-color-body {
  padding: 1rem;
}

.packata-color-body p {
  margin: 0.25rem 0;
}

.packata-color-name {
  font-size: 1.05rem;
  font-weight: 700;
  color: #293C42;
}

.packata-color-hex {
  font-family: Menlo, Monaco, Consolas, monospace;
  color: #247580;
}

.packata-usage-table {
  width: 100%;
  border-collapse: collapse;
  margin-top: 1rem;
}

.packata-usage-table th,
.packata-usage-table td {
  border: 1px solid #D5E3DF;
  padding: 0.7rem 0.85rem;
  text-align: left;
}

.packata-usage-table th {
  background: #DDEEE9;
  color: #293C42;
}

.packata-preview {
  border: 1px solid #D5E3DF;
  border-radius: 14px;
  overflow: hidden;
  margin: 2rem 0;
  background: #FAFBF8;
}

.packata-preview-header {
  background: #247580;
  color: #ffffff;
  padding: 1rem 1.25rem;
  font-weight: 700;
}

.packata-preview-body {
  padding: 1.25rem;
}

.packata-button-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  margin-top: 1rem;
}

.packata-btn {
  display: inline-block;
  padding: 0.65rem 1rem;
  border-radius: 8px;
  font-weight: 600;
  text-decoration: none !important;
  border: 1px solid transparent;
}

.packata-btn-primary {
  background: #247580;
  color: #ffffff !important;
}

.packata-btn-accent {
  background: #D99536;
  color: #293C42 !important;
}

.packata-btn-muted {
  background: #DDEEE9;
  color: #293C42 !important;
  border-color: #D5E3DF;
}

.packata-note {
  background: #F6F8F7;
  border-left: 4px solid #D99536;
  padding: 1rem 1rem;
  border-radius: 8px;
  margin: 1rem 0 1.5rem 0;
}
</style>

# Brand colors

Packata uses a calm, technical palette built around **teal**, **amber**, and **clean neutral surfaces**.
The goal is to feel both **practical for documentation** and **distinctive as a product identity**.

## Core palette

<div class="packata-color-grid">
  <div class="packata-color-card">
    <div class="packata-color-swatch" style="background:#247580; color:#ffffff;">Primary</div>
    <div class="packata-color-body">
      <p class="packata-color-name">Packata Teal</p>
      <p class="packata-color-hex">#247580</p>
      <p>Main product color. Use for navigation, primary actions, links, and emphasis.</p>
    </div>
  </div>

  <div class="packata-color-card">
    <div class="packata-color-swatch" style="background:#D99536; color:#293C42;">Accent</div>
    <div class="packata-color-body">
      <p class="packata-color-name">Packata Amber</p>
      <p class="packata-color-hex">#D99536</p>
      <p>Accent color. Use for highlights, badges, selected states, and callouts.</p>
    </div>
  </div>

  <div class="packata-color-card">
    <div class="packata-color-swatch" style="background:#293C42; color:#ffffff;">Text</div>
    <div class="packata-color-body">
      <p class="packata-color-name">Packata Ink</p>
      <p class="packata-color-hex">#293C42</p>
      <p>Primary text color. Use for headings, body text, and strong contrast.</p>
    </div>
  </div>

  <div class="packata-color-card">
    <div class="packata-color-swatch" style="background:#FAFBF8; color:#293C42; border-top:1px solid #eef2ef;">Canvas</div>
    <div class="packata-color-body">
      <p class="packata-color-name">Packata Canvas</p>
      <p class="packata-color-hex">#FAFBF8</p>
      <p>Main page background. Keeps the site light, calm, and readable.</p>
    </div>
  </div>

  <div class="packata-color-card">
    <div class="packata-color-swatch" style="background:#DDEEE9; color:#293C42;">Soft Surface</div>
    <div class="packata-color-body">
      <p class="packata-color-name">Packata Soft Mint</p>
      <p class="packata-color-hex">#DDEEE9</p>
      <p>Use for secondary surfaces such as sidebars, panels, and soft highlights.</p>
    </div>
  </div>

  <div class="packata-color-card">
    <div class="packata-color-swatch" style="background:#D5E3DF; color:#293C42;">Border</div>
    <div class="packata-color-body">
      <p class="packata-color-name">Packata Border</p>
      <p class="packata-color-hex">#D5E3DF</p>
      <p>Use for borders, dividers, cards, table separators, and subtle framing.</p>
    </div>
  </div>
</div>

## Semantic palette

<div class="packata-color-grid">
  <div class="packata-color-card">
    <div class="packata-color-swatch" style="background:#287A58; color:#ffffff;">Success</div>
    <div class="packata-color-body">
      <p class="packata-color-name">Success Green</p>
      <p class="packata-color-hex">#287A58</p>
      <p>Successful checks, validation passed, healthy state.</p>
    </div>
  </div>

  <div class="packata-color-card">
    <div class="packata-color-swatch" style="background:#B85B32; color:#ffffff;">Warning</div>
    <div class="packata-color-body">
      <p class="packata-color-name">Warning Rust</p>
      <p class="packata-color-hex">#B85B32</p>
      <p>Warnings, caution states, or things requiring attention.</p>
    </div>
  </div>

  <div class="packata-color-card">
    <div class="packata-color-swatch" style="background:#A94E5F; color:#ffffff;">Danger</div>
    <div class="packata-color-body">
      <p class="packata-color-name">Danger Rose</p>
      <p class="packata-color-hex">#A94E5F</p>
      <p>Errors, destructive actions, and failure states.</p>
    </div>
  </div>
</div>

## Usage guidance

| Role | Color | Usage |
|---|---|---|
| Primary brand color | `#247580` | Navbar, primary buttons, links, active navigation |
| Accent color | `#D99536` | Highlights, badges, selected states, callouts |
| Main text | `#293C42` | Headings and body text |
| Main background | `#FAFBF8` | Page background |
| Secondary surface | `#DDEEE9` | Panels, sidebars, cards, subtle sections |
| Border color | `#D5E3DF` | Rules, separators, table borders |
| Success | `#287A58` | Success messages and valid states |
| Warning | `#B85B32` | Warnings and caution |
| Danger | `#A94E5F` | Errors and destructive actions |

## Preview

<div class="packata-preview">
  <div class="packata-preview-header">
    Packata documentation
  </div>
  <div class="packata-preview-body">
    <h3 style="margin-top:0; color:#293C42;">Data packages, made practical.</h3>
    <p style="color:#293C42;">
      Inspect, validate and work with data packages and contracts using a unified CLI and .NET API.
    </p>

    <div class="packata-note">
      Use amber for emphasis, not as the main structural color.
    </div>

    <div class="packata-button-row">
      <span class="packata-btn packata-btn-primary">Primary action</span>
      <span class="packata-btn packata-btn-accent">Accent action</span>
      <span class="packata-btn packata-btn-muted">Secondary action</span>
    </div>
  </div>
</div>

## Accessibility notes

- Prefer **Packata Ink** (`#293C42`) for text on light backgrounds.
- Prefer **white text** on Teal, Success, Warning and Danger swatches. Use **Packata Ink** on Amber for readable contrast.
- Use **Canvas** as the main page background to preserve readability on long technical pages.
- Use **Soft Mint** for structure and grouping, not for large blocks of dense content.

## Recommendation

For Packata, the best visual balance is:

- **Teal** for structure and action
- **Amber** for emphasis
- **Off-white canvas** for reading comfort
- **Dark ink** for content
- **Soft mint** for secondary surfaces
