# Quota Watch

## 하네스: Feature Development

**목표:** 새 기능 요청에서 TDD 구현 완료까지 전체 개발 사이클을 에이전트 팀이 자동화한다.

**트리거:**
- 새 기능 개발 전체 사이클 → `superpowers:feature-development` 스킬을 사용하라
- 설계 문서만 작성 → `superpowers:design-spec`
- 구현 계획만 작성 → `superpowers:implementation-plan`
- 기존 계획 파일 실행 → `superpowers:subagent-driven-development`
- `docs/superpowers/plans/*.md` 파일을 열거나 "계획 실행" 요청 시 → `superpowers:subagent-driven-development`

단순 질문, 코드 설명, 빠른 수정은 스킬 없이 직접 응답 가능.

**변경 이력:**
| 날짜 | 변경 내용 | 대상 | 사유 |
|------|----------|------|------|
| 2026-06-10 | 초기 구성 | 전체 | 신규 구축 |
