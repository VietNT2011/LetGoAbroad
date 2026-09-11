import { useParams, Link } from 'react-router-dom';
import { useResource, Loading, ErrorNotice } from '../hooks';
import { ArrowLeft, Clock3, BookOpen } from 'lucide-react';
type LessonData = { id: string; title: string; summary: string; body: string; skill: string; minutes: number; attribution: string; version: number };
export default function Lesson() {
  const { id } = useParams(); const { data, error, loading, reload } = useResource<LessonData>(`/lessons/${id}`);
  return <><Link className="back-link" to="/catalog"><ArrowLeft size={16}/> Trở lại bài học</Link><ErrorNotice error={error} retry={reload}/>{loading ? <Loading/> : data ? <article className="lesson"><span className="eyebrow">{data.skill.toUpperCase()} · VERSION {data.version}</span><h1>{data.title}</h1><p className="lead">{data.summary}</p><div className="lesson-meta"><Clock3 size={15}/> {data.minutes} phút <BookOpen size={15}/> Nội dung nền tảng</div><div className="lesson-body">{data.body.split('\n\n').map((paragraph,i) => <p key={i}>{paragraph}</p>)}</div><div className="source-note">Nguồn / Attribution: {data.attribution}</div></article> : null}</>;
}
